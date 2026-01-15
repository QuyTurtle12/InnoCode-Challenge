using BusinessLogic.IServices.NotificationsAndLogs;
using DataAccess.Entities;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;

namespace BusinessLogic.Services.Contests
{
    public class ContestStateJob
    {
        private readonly ILogger<ContestStateJob> _logger;
        private readonly IServiceProvider _serviceProvider;

        public ContestStateJob(
            ILogger<ContestStateJob> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 60)]
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 10, 30, 60 })]
        public async Task UpdateSpecificContestAsync(Guid contestId)
        {
            try
            {
                _logger.LogInformation("Processing contest state update for {ContestId} at {Time}",
                    contestId, DateTime.UtcNow);

                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();

                IGenericRepository<Contest> contestRepo = unitOfWork.GetRepository<Contest>();
                IGenericRepository<Config> configRepo = unitOfWork.GetRepository<Config>();
                DateTime now = DateTime.UtcNow;

                Contest? contest = await contestRepo.Entities
                    .Where(c => c.ContestId == contestId && c.DeletedAt == null)
                    .FirstOrDefaultAsync();

                if (contest == null)
                {
                    _logger.LogWarning("Contest {ContestId} not found or deleted", contestId);
                    return;
                }

                // Skip if in terminal state
                if (contest.Status == ContestStatusEnum.Completed.ToString()
                    || contest.Status == ContestStatusEnum.Cancelled.ToString())
                {
                    _logger.LogDebug("Contest {ContestId} is in terminal state {Status}, skipping update",
                        contestId, contest.Status);
                    return;
                }

                List<Config> configs = await configRepo.Entities
                    .Where(c => c.Key.Contains(contestId.ToString())
                        && (c.Key.Contains("registration_start") || c.Key.Contains("registration_end"))
                        && c.DeletedAt == null)
                    .ToListAsync();

                ILookup<string, Config> configLookup = configs.ToLookup(c => c.Key);

                string? newStatus = await DetermineContestStatusAsync(unitOfWork, contest, now, configLookup);

                if (newStatus != null && newStatus != contest.Status)
                {
                    string oldStatus = contest.Status;
                    contest.Status = newStatus;
                    await contestRepo.UpdateAsync(contest);
                    await unitOfWork.SaveAsync();

                    _logger.LogInformation(
                        "Contest {ContestId} ({ContestName}) status changed from {OldStatus} to {NewStatus}",
                        contest.ContestId, contest.Name, oldStatus, newStatus);

                    // If transitioning to Delayed, cancel all scheduled jobs
                    if (newStatus == ContestStatusEnum.Delayed.ToString())
                    {
                        await DeleteContestScheduledJobsAsync(unitOfWork, contestId);

                        _logger.LogWarning(
                            "Contest {ContestId} ({ContestName}) moved to Delayed status due to no team registrations. All scheduled jobs cancelled.",
                            contest.ContestId, contest.Name);
                    }
                    else
                    {
                        // Schedule next state transition for other statuses
                        await ScheduleNextStateTransitionAsync(contest, configLookup);
                    }

                    // Perform status-specific actions
                    await HandleStatusTransitionAsync(scope, unitOfWork, contest, oldStatus, newStatus, configLookup);
                }
                else
                {
                    _logger.LogDebug("Contest {ContestId} status unchanged ({Status})",
                        contestId, contest.Status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UpdateSpecificContestAsync for contest {ContestId}", contestId);
                throw;
            }
        }

        private static async Task<bool> CheckContestHasTeamsAsync(IUOW unitOfWork, Guid contestId)
        {
            IGenericRepository<Team> teamRepo = unitOfWork.GetRepository<Team>();

            // Check if contest has at least 1 non-deleted team
            int teamCount = await teamRepo.Entities
                .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                .CountAsync();

            return teamCount > 0;
        }

        private async Task DeleteContestScheduledJobsAsync(IUOW unitOfWork, Guid contestId)
        {
            try
            {
                // Get all rounds for the contest
                IGenericRepository<Round> roundRepo = unitOfWork.GetRepository<Round>();
                List<Guid> roundIds = await roundRepo.Entities
                    .Where(r => r.ContestId == contestId && r.DeletedAt == null)
                    .Select(r => r.RoundId)
                    .ToListAsync();

                // Delete contest state transition jobs
                string contestJobId = $"contest-state-{contestId}";
                BackgroundJob.Delete(contestJobId);

                // Delete round state transition jobs
                foreach (Guid roundId in roundIds)
                {
                    string roundJobId = $"round-state-{roundId}";
                    BackgroundJob.Delete(roundJobId);
                }

                _logger.LogInformation(
                    "Deleted scheduled jobs for contest {ContestId} and {RoundCount} rounds",
                    contestId, roundIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete scheduled jobs for contest {ContestId}", contestId);
            }
        }

        public async Task ScheduleContestStateTransitionsAsync(Guid contestId)
        {
            try
            {
                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();

                IGenericRepository<Contest> contestRepo = unitOfWork.GetRepository<Contest>();
                IGenericRepository<Config> configRepo = unitOfWork.GetRepository<Config>();

                Contest? contest = await contestRepo.Entities
                    .Where(c => c.ContestId == contestId && c.DeletedAt == null)
                    .FirstOrDefaultAsync();

                if (contest == null)
                {
                    _logger.LogWarning("Contest {ContestId} not found for scheduling", contestId);
                    return;
                }

                List<Config> configs = await configRepo.Entities
                    .Where(c => c.Key.Contains(contestId.ToString())
                        && (c.Key.Contains("registration_start") || c.Key.Contains("registration_end"))
                        && c.DeletedAt == null)
                    .ToListAsync();

                ILookup<string, Config> configLookup = configs.ToLookup(c => c.Key);

                await ScheduleNextStateTransitionAsync(contest, configLookup);

                _logger.LogInformation("Scheduled state transitions for contest {ContestId} ({ContestName})",
                    contest.ContestId, contest.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scheduling state transitions for contest {ContestId}", contestId);
            }
        }

        private async Task HandleStatusTransitionAsync(
            IServiceScope scope,
            IUOW unitOfWork,
            Contest contest,
            string oldStatus,
            string newStatus,
            ILookup<string, Config> configLookup)
        {
            try
            {
                var notif = scope.ServiceProvider.GetRequiredService<INotificationService>();

                async Task<List<Guid>> GetParticipantIdsAsync()
                {
                    IGenericRepository<Team> teamRepo = unitOfWork.GetRepository<Team>();

                    List<Guid> studentIds = await teamRepo.Entities
                        .AsNoTracking()
                        .Where(t => t.ContestId == contest.ContestId && t.DeletedAt == null)
                        .SelectMany(t => t.TeamMembers
                            .Select(tm => tm.Student.UserId))
                        .ToListAsync();

                    List<Guid> mentorIds = await teamRepo.Entities
                        .AsNoTracking()
                        .Where(t => t.ContestId == contest.ContestId && t.DeletedAt == null)
                        .Select(t => t.Mentor.UserId)
                        .ToListAsync();

                    return studentIds.Concat(mentorIds).Where(x => x != Guid.Empty).Distinct().ToList();
                }

                var payloadBase = new
                {
                    contestId = contest.ContestId,
                    name = contest.Name,
                    oldStatus,
                    newStatus,
                    targetType = TargetTypes.Contest,
                    targetId = contest.ContestId.ToString()
                };

                // Registration Open 
                if (newStatus == ContestStatusEnum.RegistrationOpen.ToString())
                {
                    // Notify organizer
                    if (Guid.TryParse(contest.CreatedBy, out var organizerId) && organizerId != Guid.Empty)
                    {
                        await notif.CreateInAppToUserAsync(organizerId, NotificationTypes.ContestRegistrationOpen, new
                        {
                            payloadBase.contestId,
                            payloadBase.name,
                            payloadBase.targetType,
                            payloadBase.targetId,
                            message = $"Registration opened for '{contest.Name}'."
                        });
                    }
                }

                // Registration Closed 
                if (newStatus == ContestStatusEnum.RegistrationClosed.ToString())
                {
                    // Validate and disqualify teams not meeting minimum requirements
                    await ValidateAndDisqualifyTeamsAsync(unitOfWork, contest.ContestId);

                    List<Guid> participantIds = await GetParticipantIdsAsync();

                    // Notify participants
                    if (participantIds.Count > 0)
                    {
                        await notif.CreateInAppToUsersAsync(participantIds, NotificationTypes.ContestRegistrationClosed, new
                        {
                            payloadBase.contestId,
                            payloadBase.name,
                            payloadBase.targetType,
                            payloadBase.targetId,
                            message = $"Registration closed for '{contest.Name}'."
                        });
                    }
                }

                // Contest Ongoing 
                if (newStatus == ContestStatusEnum.Ongoing.ToString())
                {
                    // Notify participants
                    List<Guid> participantIds = await GetParticipantIdsAsync();
                    if (participantIds.Count > 0)
                    {
                        await notif.CreateInAppToUsersAsync(participantIds, NotificationTypes.ContestStarted, new
                        {
                            payloadBase.contestId,
                            payloadBase.name,
                            payloadBase.targetType,
                            payloadBase.targetId,
                            message = $"Contest '{contest.Name}' has started."
                        });
                    }
                }

                // Contest Completed 
                if (newStatus == ContestStatusEnum.Completed.ToString())
                {
                    // Notify participants
                    List<Guid> participantIds = await GetParticipantIdsAsync();
                    if (participantIds.Count > 0)
                    {
                        await notif.CreateInAppToUsersAsync(participantIds, NotificationTypes.ContestEnded, new
                        {
                            payloadBase.contestId,
                            payloadBase.name,
                            payloadBase.targetType,
                            payloadBase.targetId,
                            message = $"Contest '{contest.Name}' has ended."
                        });
                    }
                }

                // Contest Delayed
                if (newStatus == ContestStatusEnum.Delayed.ToString())
                {
                    // Notify organizer
                    if (Guid.TryParse(contest.CreatedBy, out var organizerId) && organizerId != Guid.Empty)
                    {
                        await notif.CreateInAppToUserAsync(organizerId, NotificationTypes.ContestDelayed, new
                        {
                            payloadBase.contestId,
                            payloadBase.name,
                            payloadBase.targetType,
                            payloadBase.targetId,
                            message = $"Contest '{contest.Name}' has been delayed due to insufficient team registrations."
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error sending contest transition notifications. ContestId={ContestId}, Old={Old}, New={New}",
                    contest.ContestId, oldStatus, newStatus);
            }
        }


        private async Task ScheduleNextStateTransitionAsync(Contest contest, ILookup<string, Config> configLookup)
        {
            DateTime now = DateTime.UtcNow;
            var schedulePoints = new List<(DateTime Time, string Description)>();

            // Get registration dates
            string regStartKey = ConfigKeys.ContestRegStart(contest.ContestId);
            string regEndKey = ConfigKeys.ContestRegEnd(contest.ContestId);

            if (DateTime.TryParse(configLookup[regStartKey].FirstOrDefault()?.Value, out DateTime regStart)
                && regStart > now)
            {
                schedulePoints.Add((regStart, "Registration Open"));
            }

            if (DateTime.TryParse(configLookup[regEndKey].FirstOrDefault()?.Value, out DateTime regEnd)
                && regEnd > now)
            {
                schedulePoints.Add((regEnd, "Registration Closed"));
            }

            // Contest start
            if (contest.Start.HasValue && contest.Start.Value > now)
            {
                schedulePoints.Add((contest.Start.Value, "Contest Ongoing"));
            }

            // Contest end
            if (contest.End.HasValue && contest.End.Value > now)
            {
                schedulePoints.Add((contest.End.Value, "Contest Completed"));
            }

            // Schedule jobs for each transition point
            foreach (var (time, description) in schedulePoints.OrderBy(x => x.Time))
            {
                TimeSpan delay = time - now;
                if (delay.TotalSeconds > 0)
                {
                    BackgroundJob.Schedule<ContestStateJob>(
                        job => job.UpdateSpecificContestAsync(contest.ContestId),
                        delay);

                    _logger.LogInformation(
                        "Scheduled '{Description}' for contest {ContestId} at {Time} (in {Minutes:F2} minutes)",
                        description, contest.ContestId, time, delay.TotalMinutes);
                }
            }
        }

        private static async Task<string?> DetermineContestStatusAsync(
            IUOW unitOfWork,
            Contest contest,
            DateTime now,
            ILookup<string, Config> configLookup)
        {
            // Don't change terminal or managed states
            if (contest.Status == ContestStatusEnum.Completed.ToString()
                || contest.Status == ContestStatusEnum.Cancelled.ToString()
                || contest.Status == ContestStatusEnum.Paused.ToString()
                || contest.Status == ContestStatusEnum.Draft.ToString()
                || contest.Status == ContestStatusEnum.Delayed.ToString())
                return null;

            string regStartKey = ConfigKeys.ContestRegStart(contest.ContestId);
            string regEndKey = ConfigKeys.ContestRegEnd(contest.ContestId);

            DateTime? registrationStart = null;
            DateTime? registrationEnd = null;

            if (DateTime.TryParse(configLookup[regStartKey].FirstOrDefault()?.Value, out DateTime regStart))
            {
                registrationStart = regStart;
            }

            if (DateTime.TryParse(configLookup[regEndKey].FirstOrDefault()?.Value, out DateTime regEnd))
            {
                registrationEnd = regEnd;
            }

            // Priority 1: Contest has ended
            if (contest.End.HasValue && now >= contest.End.Value)
            {
                return ContestStatusEnum.Completed.ToString();
            }

            // Priority 2: Contest is ongoing
            if (contest.Start.HasValue && contest.End.HasValue
                && now >= contest.Start.Value && now < contest.End.Value)
            {
                return ContestStatusEnum.Ongoing.ToString();
            }

            // Priority 3: Registration closed, contest not started
            if (registrationEnd.HasValue && now >= registrationEnd.Value
                && contest.Start.HasValue && now < contest.Start.Value)
            {
                // Check if contest has at least 1 team
                bool hasTeams = await CheckContestHasTeamsAsync(unitOfWork, contest.ContestId);

                if (!hasTeams)
                {
                    // No teams registered, move to Delayed status
                    return ContestStatusEnum.Delayed.ToString();
                }

                return ContestStatusEnum.RegistrationClosed.ToString();
            }

            // Priority 4: Registration is open
            if (registrationStart.HasValue && registrationEnd.HasValue
                && now >= registrationStart.Value && now < registrationEnd.Value
                && contest.Status == ContestStatusEnum.Published.ToString())
            {
                return ContestStatusEnum.RegistrationOpen.ToString();
            }

            return null;
        }

        private async Task ValidateAndDisqualifyTeamsAsync(IUOW unitOfWork, Guid contestId)
        {
            try
            {
                IGenericRepository<Config> configRepo = unitOfWork.GetRepository<Config>();
                IGenericRepository<Team> teamRepo = unitOfWork.GetRepository<Team>();
                IGenericRepository<TeamMember> teamMemberRepo = unitOfWork.GetRepository<TeamMember>();

                // Get team members min requirement
                string? membersMinContest = await configRepo.Entities
                    .Where(c => c.Key == ConfigKeys.ContestTeamMembersMin(contestId) && c.DeletedAt == null)
                    .Select(c => c.Value)
                    .FirstOrDefaultAsync();

                string? membersMinDefault = await configRepo.Entities
                    .Where(c => c.Key == ConfigKeys.Defaults_TeamMembersMin && c.DeletedAt == null)
                    .Select(c => c.Value)
                    .FirstOrDefaultAsync();

                int minMembers = 1;
                if (!string.IsNullOrEmpty(membersMinContest))
                {
                    int.TryParse(membersMinContest, out minMembers);
                }
                else if (!string.IsNullOrEmpty(membersMinDefault))
                {
                    int.TryParse(membersMinDefault, out minMembers);
                }

                // Get all teams for this contest
                List<Team> teams = await teamRepo.Entities
                    .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                    .ToListAsync();

                List<string> disqualifiedTeamNames = new List<string>();

                foreach (Team team in teams)
                {
                    // Count team members
                    int memberCount = await teamMemberRepo.Entities
                        .Where(tm => tm.TeamId == team.TeamId)
                        .CountAsync();

                    // If team doesn't meet minimum requirement, disqualify it
                    if (memberCount < minMembers)
                    {
                        team.Status = TeamStatusConstants.Disqualified;
                        await teamRepo.UpdateAsync(team);
                        disqualifiedTeamNames.Add(team.Name);
                    }
                }

                // Save changes
                if (disqualifiedTeamNames.Any())
                {
                    await unitOfWork.SaveAsync();

                    _logger.LogWarning(
                        "Disqualified {Count} team(s) in contest {ContestId} for not meeting minimum member requirement ({MinMembers} members): {Teams}",
                        disqualifiedTeamNames.Count, contestId, minMembers, string.Join(", ", disqualifiedTeamNames));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating and disqualifying teams for contest {ContestId}", contestId);
            }
        }
    }
}