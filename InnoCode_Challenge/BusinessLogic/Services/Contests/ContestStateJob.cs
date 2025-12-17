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

        /// <summary>
        /// Updates a specific contest's state. Protected against concurrent execution.
        /// This is the single source of truth for contest state transitions.
        /// </summary>
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

                string? newStatus = DetermineContestStatus(contest, now, configLookup);

                if (newStatus != null && newStatus != contest.Status)
                {
                    string oldStatus = contest.Status;
                    contest.Status = newStatus;
                    await contestRepo.UpdateAsync(contest);
                    await unitOfWork.SaveAsync();

                    _logger.LogInformation(
                        "Contest {ContestId} ({ContestName}) status changed from {OldStatus} to {NewStatus}",
                        contest.ContestId, contest.Name, oldStatus, newStatus);

                    // Schedule next state transition
                    await ScheduleNextStateTransitionAsync(contest, configLookup);

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

        /// <summary>
        /// Handles status-specific actions when a contest transitions to a new state.
        /// </summary>
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
                    var teamRepo = unitOfWork.GetRepository<Team>();

                    var studentIds = await teamRepo.Entities
                        .AsNoTracking()
                        .Where(t => t.ContestId == contest.ContestId && t.DeletedAt == null)
                        .SelectMany(t => t.TeamMembers
                            .Select(tm => tm.Student.UserId))
                        .ToListAsync();

                    var mentorIds = await teamRepo.Entities
                        .AsNoTracking()
                        .Where(t => t.ContestId == contest.ContestId && t.DeletedAt == null && t.MentorId != null)
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

                // 1) Registration Open 
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

                // 2) Registration Closed 
                if (newStatus == ContestStatusEnum.RegistrationClosed.ToString())
                {
                    var participantIds = await GetParticipantIdsAsync();

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

                // 3) Contest Ongoing 
                if (newStatus == ContestStatusEnum.Ongoing.ToString())
                {
                    // Notify participants
                    var participantIds = await GetParticipantIdsAsync();
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

                // 4) Contest Completed 
                if (newStatus == ContestStatusEnum.Completed.ToString())
                {
                    // Notify participants
                    var participantIds = await GetParticipantIdsAsync();
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

        private static string? DetermineContestStatus(
            Contest contest,
            DateTime now,
            ILookup<string, Config> configLookup)
        {
            // Don't change terminal or managed states
            if (contest.Status == ContestStatusEnum.Completed.ToString()
                || contest.Status == ContestStatusEnum.Cancelled.ToString()
                || contest.Status == ContestStatusEnum.Paused.ToString()
                || contest.Status == ContestStatusEnum.Draft.ToString())
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
    }
}