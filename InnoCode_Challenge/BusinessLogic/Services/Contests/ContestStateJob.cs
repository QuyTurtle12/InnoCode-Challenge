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
                    await HandleStatusTransitionAsync(contest, oldStatus, newStatus);
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
        private async Task HandleStatusTransitionAsync(Contest contest, string oldStatus, string newStatus)
        {
            try
            {
                // Example: When contest completes, you might want to trigger certificate generation
                if (newStatus == ContestStatusEnum.Completed.ToString())
                {
                    _logger.LogInformation(
                        "Contest {ContestId} completed. Triggering post-completion tasks.",
                        contest.ContestId);

                    // Example: Enqueue certificate generation job
                    // BackgroundJob.Enqueue<CertificateJob>(job => 
                    //     job.GenerateCertificatesForContestAsync(contest.ContestId));
                }

                // Example: When registration opens, send notifications
                if (newStatus == ContestStatusEnum.RegistrationOpen.ToString())
                {
                    _logger.LogInformation(
                        "Registration opened for contest {ContestId}. Sending notifications.",
                        contest.ContestId);

                    // Example: Enqueue notification job
                    // BackgroundJob.Enqueue<NotificationJob>(job => 
                    //     job.SendRegistrationOpenNotificationsAsync(contest.ContestId));
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error handling status transition for contest {ContestId} from {OldStatus} to {NewStatus}",
                    contest.ContestId, oldStatus, newStatus);
                // Don't throw - we don't want to fail the main status update
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