using DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;

namespace BusinessLogic.Services.Contests
{
    public class ContestStateBackgroundService : BackgroundService
    {
        private readonly ILogger<ContestStateBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5); // Check every 5 minutes

        public ContestStateBackgroundService(
            ILogger<ContestStateBackgroundService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Contest State Background Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await UpdateContestStatesAsync();
                    await UpdateRoundStatesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while updating contest states.");
                }

                // Wait for the next check interval
                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Contest State Background Service is stopping.");
        }

        private async Task UpdateContestStatesAsync()
        {
            try
            {
                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();

                IGenericRepository<Contest> contestRepo = unitOfWork.GetRepository<Contest>();
                IGenericRepository<Config> configRepo = unitOfWork.GetRepository<Config>();
                IGenericRepository<Round> roundRepo = unitOfWork.GetRepository<Round>();
                DateTime now = DateTime.UtcNow;

                // Get all active contests that might need state updates
                List<Contest> contests = await contestRepo.Entities
                    .Where(c => c.DeletedAt == null
                        && c.Status != ContestStatusEnum.Completed.ToString()
                        && c.Status != ContestStatusEnum.Cancelled.ToString())
                    .Include(c => c.Rounds) // Include rounds to check their status
                    .ToListAsync();

                // Extract contest IDs for batch config loading
                List<Guid> contestIds = contests.Select(c => c.ContestId).ToList();

                // Load all registration configs in one query
                List<Config> configs = await configRepo.Entities
                    .Where(c => contestIds.Any(id => c.Key.Contains(id.ToString()))
                        && (c.Key.Contains("registration_start") || c.Key.Contains("registration_end"))
                        && c.DeletedAt == null)
                    .ToListAsync();

                // Create lookup for faster access
                ILookup<string, Config> configLookup = configs.ToLookup(c => c.Key);

                int updatedCount = 0;

                // Iterate through contests and determine if status needs to be updated
                foreach (Contest contest in contests)
                {
                    string? newStatus = await DetermineContestStatusAsync(contest, now, configLookup);

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
                    _logger.LogInformation("Updated {Count} contest(s) status.", updatedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UpdateContestStatesAsync");
            }
        }

        private static Task<string?> DetermineContestStatusAsync(
            Contest contest,
            DateTime now,
            ILookup<string, Config> configLookup)
        {
            // Skip if already in terminal state OR manually paused
            if (contest.Status == ContestStatusEnum.Completed.ToString()
                || contest.Status == ContestStatusEnum.Cancelled.ToString()
                || contest.Status == ContestStatusEnum.Paused.ToString())
                return Task.FromResult<string?>(null);

            // Get registration dates from config
            string regStartKey = ConfigKeys.ContestRegStart(contest.ContestId);
            string regEndKey = ConfigKeys.ContestRegEnd(contest.ContestId);

            Config? regStartConfig = configLookup[regStartKey].FirstOrDefault();
            Config? regEndConfig = configLookup[regEndKey].FirstOrDefault();

            DateTime? registrationStart = null;
            DateTime? registrationEnd = null;

            if (regStartConfig != null && DateTime.TryParse(regStartConfig.Value, out DateTime regStart))
            {
                registrationStart = regStart;
            }

            if (regEndConfig != null && DateTime.TryParse(regEndConfig.Value, out DateTime regEnd))
            {
                registrationEnd = regEnd;
            }

            // Check if all rounds have ended (for leaderboard freeze)
            bool allRoundsEnded = contest.Rounds != null
                && contest.Rounds.Any()
                && contest.Rounds.Where(r => !r.DeletedAt.HasValue).All(r => now >= r.End);

            // Priority 1: Check if all rounds have ended (leaderboard should be frozen)
            if (allRoundsEnded && contest.Status == ContestStatusEnum.Ongoing.ToString())
            {
                // Mark as completed when all rounds are finished
                return Task.FromResult<string?>(ContestStatusEnum.Completed.ToString());
            }

            // Priority 2: Check if contest has ended (terminal state)
            if (contest.End.HasValue && now >= contest.End.Value)
            {
                return Task.FromResult<string?>(ContestStatusEnum.Completed.ToString());
            }

            // Priority 3: Check if contest is ongoing
            if (contest.Start.HasValue && now >= contest.Start.Value && now < contest.End)
            {
                return Task.FromResult<string?>(ContestStatusEnum.Ongoing.ToString());
            }

            // Priority 4: Check if registration has closed but contest hasn't started
            if (registrationEnd.HasValue && now >= registrationEnd.Value
                && contest.Start.HasValue && now < contest.Start.Value
                && contest.Status != ContestStatusEnum.RegistrationClosed.ToString())
            {
                return Task.FromResult<string?>(ContestStatusEnum.RegistrationClosed.ToString());
            }

            // Priority 5: Check if registration is open
            if (registrationStart.HasValue && registrationEnd.HasValue
                && now >= registrationStart.Value && now < registrationEnd.Value
                && contest.Status == ContestStatusEnum.Published.ToString())
            {
                return Task.FromResult<string?>(ContestStatusEnum.RegistrationOpen.ToString());
            }

            // No state change needed
            return Task.FromResult<string?>(null);
        }

        private async Task UpdateRoundStatesAsync()
        {
            try
            {
                // Create a new scope to get scoped services
                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();
                IGenericRepository<Round> roundRepo = unitOfWork.GetRepository<Round>();

                DateTime now = DateTime.UtcNow;

                // Get all active rounds (not deleted)
                List<Round> rounds = await roundRepo.Entities
                    .Where(r => r.DeletedAt == null)
                    .ToListAsync();

                int updatedCount = 0;

                // Iterate through rounds and determine if status needs to be updated
                foreach (Round round in rounds)
                {
                    string? newStatus = DetermineRoundStatus(round, now);

                    if (newStatus != null && newStatus != round.Status)
                    {
                        string oldStatus = round.Status ?? "null";
                        round.Status = newStatus;
                        await roundRepo.UpdateAsync(round);
                        updatedCount++;

                        _logger.LogInformation(
                            "Round {RoundId} ({RoundName}) status changed from {OldStatus} to {NewStatus}",
                            round.RoundId, round.Name, oldStatus, newStatus);
                    }
                }

                if (updatedCount > 0)
                {
                    await unitOfWork.SaveAsync();
                    _logger.LogInformation("Updated {Count} round(s) status.", updatedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UpdateRoundStatesAsync");
            }
        }

        private static string? DetermineRoundStatus(Round round, DateTime now)
        {
            // If round hasn't started yet or has already ended, return Closed
            if (now < round.Start || now >= round.End)
            {
                return RoundStatusEnum.Closed.ToString();
            }

            // If current time is within the round's start and end time, return Opened
            if (now >= round.Start && now < round.End)
            {
                return RoundStatusEnum.Opened.ToString();
            }

            // No status change needed
            return null;
        }
    }
}