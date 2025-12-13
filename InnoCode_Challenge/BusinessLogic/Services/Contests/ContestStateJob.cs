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

        public async Task UpdateAllContestStatesAsync()
        {
            try
            {
                _logger.LogInformation("Starting bulk contest state update job at {Time}", DateTime.UtcNow);

                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();

                IGenericRepository<Contest> contestRepo = unitOfWork.GetRepository<Contest>();
                IGenericRepository<Config> configRepo = unitOfWork.GetRepository<Config>();
                DateTime now = DateTime.UtcNow;

                List<Contest> contests = await contestRepo.Entities
                    .Where(c => c.DeletedAt == null
                        && c.Status != ContestStatusEnum.Completed.ToString()
                        && c.Status != ContestStatusEnum.Cancelled.ToString())
                    .Include(c => c.Rounds)
                    .ToListAsync();

                if (!contests.Any())
                {
                    _logger.LogDebug("No active contests found for state update");
                    return;
                }

                List<Guid> contestIds = contests.Select(c => c.ContestId).ToList();

                List<Config> configs = await configRepo.Entities
                    .Where(c => contestIds.Any(id => c.Key.Contains(id.ToString()))
                        && (c.Key.Contains("registration_start") || c.Key.Contains("registration_end"))
                        && c.DeletedAt == null)
                    .ToListAsync();

                ILookup<string, Config> configLookup = configs.ToLookup(c => c.Key);

                int updatedCount = 0;

                foreach (Contest contest in contests)
                {
                    string? newStatus = DetermineContestStatus(contest, now, configLookup);

                    if (newStatus != null && newStatus != contest.Status)
                    {
                        string oldStatus = contest.Status;
                        contest.Status = newStatus;
                        await contestRepo.UpdateAsync(contest);
                        updatedCount++;

                        _logger.LogInformation(
                            "Contest {ContestId} ({ContestName}) status changed from {OldStatus} to {NewStatus}",
                            contest.ContestId, contest.Name, oldStatus, newStatus);
                    }
                }

                if (updatedCount > 0)
                {
                    await unitOfWork.SaveAsync();
                    _logger.LogInformation("Updated {Count} contest(s) status at {Time}",
                        updatedCount, DateTime.UtcNow);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UpdateAllContestStatesAsync job");
                throw;
            }
        }

        public async Task UpdateSpecificContestAsync(Guid contestId)
        {
            try
            {
                _logger.LogInformation("Starting specific contest state update for {ContestId} at {Time}",
                    contestId, DateTime.UtcNow);

                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();

                IGenericRepository<Contest> contestRepo = unitOfWork.GetRepository<Contest>();
                IGenericRepository<Config> configRepo = unitOfWork.GetRepository<Config>();
                DateTime now = DateTime.UtcNow;

                Contest? contest = await contestRepo.Entities
                    .Where(c => c.ContestId == contestId && c.DeletedAt == null)
                    .Include(c => c.Rounds)
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

                    // Schedule next state transition if needed
                    await ScheduleNextStateTransitionAsync(contest, configLookup);
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
                schedulePoints.Add((contest.Start.Value, "Contest Start"));
            }

            // Contest end
            if (contest.End.HasValue && contest.End.Value > now)
            {
                schedulePoints.Add((contest.End.Value, "Contest End"));
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
                        "Scheduled '{Description}' for contest {ContestId} at {Time} (in {Minutes} minutes)",
                        description, contest.ContestId, time, delay.TotalMinutes);
                }
            }
        }

        private static string? DetermineContestStatus(
            Contest contest,
            DateTime now,
            ILookup<string, Config> configLookup)
        {
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

            if (contest.End.HasValue && now >= contest.End.Value)
            {
                return ContestStatusEnum.Completed.ToString();
            }

            if (contest.Start.HasValue && now >= contest.Start.Value && now < contest.End)
            {
                return ContestStatusEnum.Ongoing.ToString();
            }

            if (registrationEnd.HasValue && now >= registrationEnd.Value
                && contest.Start.HasValue && now < contest.Start.Value
                && contest.Status != ContestStatusEnum.RegistrationClosed.ToString())
            {
                return ContestStatusEnum.RegistrationClosed.ToString();
            }

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
