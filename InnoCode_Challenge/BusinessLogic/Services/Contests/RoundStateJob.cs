using BusinessLogic.IServices;
using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Repository.IRepositories;
using Utility.Enums;

namespace BusinessLogic.Services.Contests
{
    public class RoundStateJob
    {
        private readonly ILogger<RoundStateJob> _logger;
        private readonly IServiceProvider _serviceProvider;

        public RoundStateJob(
            ILogger<RoundStateJob> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task UpdateAllRoundStatesAsync()
        {
            try
            {
                _logger.LogInformation("Starting bulk round state update job at {Time}", DateTime.UtcNow);

                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();
                IRoundService roundService = scope.ServiceProvider.GetRequiredService<IRoundService>();

                IGenericRepository<Round> roundRepo = unitOfWork.GetRepository<Round>();
                DateTime now = DateTime.UtcNow;

                List<Round> rounds = await roundRepo.Entities
                    .Where(r => r.DeletedAt == null)
                    .ToListAsync();

                if (!rounds.Any())
                {
                    _logger.LogDebug("No active rounds found for state update");
                    return;
                }

                int updatedCount = 0;

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

                    // Generate open code when round transitions to Opened status
                    if (round.Status == RoundStatusEnum.Opened.ToString())
                    {
                        try
                        {
                            await roundService.GenerateOpenCode(round.RoundId);
                            _logger.LogInformation(
                                "Successfully generated open code for round {RoundId} ({RoundName})",
                                round.RoundId, round.Name);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Failed to generate open code for round {RoundId} ({RoundName})",
                                round.RoundId, round.Name);
                        }
                    }
                }

                if (updatedCount > 0)
                {
                    await unitOfWork.SaveAsync();
                    _logger.LogInformation("Updated {Count} round(s) status at {Time}",
                        updatedCount, DateTime.UtcNow);
                }

                // Check and distribute submissions for ended rounds
                await CheckAndDistributeSubmissionsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UpdateAllRoundStatesAsync job");
                throw;
            }
        }

        public async Task UpdateSpecificRoundAsync(Guid roundId)
        {
            try
            {
                _logger.LogInformation("Starting specific round state update for {RoundId} at {Time}",
                    roundId, DateTime.UtcNow);

                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();
                IRoundService roundService = scope.ServiceProvider.GetRequiredService<IRoundService>();

                IGenericRepository<Round> roundRepo = unitOfWork.GetRepository<Round>();
                DateTime now = DateTime.UtcNow;

                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId && r.DeletedAt == null)
                    .FirstOrDefaultAsync();

                if (round == null)
                {
                    _logger.LogWarning("Round {RoundId} not found or deleted", roundId);
                    return;
                }

                string? newStatus = DetermineRoundStatus(round, now);

                if (newStatus != null && newStatus != round.Status)
                {
                    string oldStatus = round.Status ?? "null";
                    round.Status = newStatus;
                    await roundRepo.UpdateAsync(round);
                    await unitOfWork.SaveAsync();

                    _logger.LogInformation(
                        "Round {RoundId} ({RoundName}) status changed from {OldStatus} to {NewStatus}",
                        round.RoundId, round.Name, oldStatus, newStatus);

                    // Schedule next state transition
                    await ScheduleNextStateTransitionAsync(round);

                    // Generate open code if round just opened
                    if (newStatus == RoundStatusEnum.Opened.ToString())
                    {
                        try
                        {
                            await roundService.GenerateOpenCode(round.RoundId);
                            _logger.LogInformation(
                                "Successfully generated open code for round {RoundId} ({RoundName})",
                                round.RoundId, round.Name);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Failed to generate open code for round {RoundId} ({RoundName})",
                                round.RoundId, round.Name);
                        }
                    }

                    // If round just closed, trigger submission distribution for manual problems
                    if (newStatus == RoundStatusEnum.Closed.ToString())
                    {
                        BackgroundJob.Enqueue<RoundStateJob>(job =>
                            job.CheckAndDistributeSubmissionsForRoundAsync(roundId));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UpdateSpecificRoundAsync for round {RoundId}", roundId);
                throw;
            }
        }

        public async Task ScheduleRoundStateTransitionsAsync(Guid roundId)
        {
            try
            {
                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();

                IGenericRepository<Round> roundRepo = unitOfWork.GetRepository<Round>();

                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId && r.DeletedAt == null)
                    .FirstOrDefaultAsync();

                if (round == null)
                {
                    _logger.LogWarning("Round {RoundId} not found for scheduling", roundId);
                    return;
                }

                await ScheduleNextStateTransitionAsync(round);

                _logger.LogInformation("Scheduled state transitions for round {RoundId} ({RoundName})",
                    round.RoundId, round.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scheduling state transitions for round {RoundId}", roundId);
            }
        }

        private static string? DetermineRoundStatus(Round round, DateTime now)
        {
            // Priority 1: Check if round hasn't started yet (Incoming)
            if (now < round.Start)
            {
                return RoundStatusEnum.Incoming.ToString();
            }

            // Priority 2: Check if round is currently ongoing (Opened)
            if (now >= round.Start && now < round.End)
            {
                return RoundStatusEnum.Opened.ToString();
            }

            // Priority 3: Round has ended (Closed)
            if (now >= round.End)
            {
                return RoundStatusEnum.Closed.ToString();
            }

            return null;
        }

        private async Task ScheduleNextStateTransitionAsync(Round round)
        {
            DateTime now = DateTime.UtcNow;
            var schedulePoints = new List<(DateTime Time, string Description)>();

            // Round start (Incoming -> Opened)
            if (round.Start > now)
            {
                schedulePoints.Add((round.Start, "Round Start (Opened)"));
            }

            // Round end (Opened -> Closed)
            if (round.End > now)
            {
                schedulePoints.Add((round.End, "Round End (Closed)"));
            }

            // Schedule jobs for each transition point
            foreach (var (time, description) in schedulePoints.OrderBy(x => x.Time))
            {
                var delay = time - now;
                if (delay.TotalSeconds > 0)
                {
                    BackgroundJob.Schedule<RoundStateJob>(
                        job => job.UpdateSpecificRoundAsync(round.RoundId),
                        delay);

                    _logger.LogInformation(
                        "Scheduled '{Description}' for round {RoundId} at {Time} (in {Minutes} minutes)",
                        description, round.RoundId, time, delay.TotalMinutes);
                }
            }
        }

        /// <summary>
        /// Checks and distributes submissions for all ended rounds with manual problems
        /// </summary>
        private async Task CheckAndDistributeSubmissionsAsync()
        {
            using IServiceScope scope = _serviceProvider.CreateScope();

            IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();
            IRoundService roundService = scope.ServiceProvider.GetRequiredService<IRoundService>();
            IConfigService configService = scope.ServiceProvider.GetRequiredService<IConfigService>();

            DateTime now = DateTime.UtcNow;

            List<Round> endedRounds = await unitOfWork.GetRepository<Round>()
                .Entities
                .Include(r => r.Problem)
                .Where(r => !r.DeletedAt.HasValue
                    && r.End <= now
                    && r.Problem != null
                    && r.Problem.Type == ProblemTypeEnum.Manual.ToString())
                .ToListAsync();

            foreach (Round round in endedRounds)
            {
                try
                {
                    bool alreadyDistributed = await configService.AreSubmissionsDistributedAsync(round.RoundId);

                    if (alreadyDistributed)
                    {
                        continue;
                    }

                    _logger.LogInformation(
                        "Distributing submissions for round {RoundId} ({RoundName})",
                        round.RoundId, round.Name);

                    await roundService.DistributeSubmissionsToJudgesAsync(round.RoundId);

                    _logger.LogInformation(
                        "Successfully distributed submissions for round {RoundId}",
                        round.RoundId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to distribute submissions for round {RoundId}", round.RoundId);
                }
            }
        }

        /// <summary>
        /// Checks and distributes submissions for a specific round (event-driven)
        /// </summary>
        public async Task CheckAndDistributeSubmissionsForRoundAsync(Guid roundId)
        {
            try
            {
                using IServiceScope scope = _serviceProvider.CreateScope();

                IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();
                IRoundService roundService = scope.ServiceProvider.GetRequiredService<IRoundService>();
                IConfigService configService = scope.ServiceProvider.GetRequiredService<IConfigService>();

                Round? round = await unitOfWork.GetRepository<Round>()
                    .Entities
                    .Include(r => r.Problem)
                    .Where(r => r.RoundId == roundId && !r.DeletedAt.HasValue)
                    .FirstOrDefaultAsync();

                if (round == null)
                {
                    _logger.LogWarning("Round {RoundId} not found for submission distribution", roundId);
                    return;
                }

                // Only process manual problems
                if (round.Problem?.Type != ProblemTypeEnum.Manual.ToString())
                {
                    _logger.LogDebug("Round {RoundId} does not have manual problem type, skipping distribution", roundId);
                    return;
                }

                bool alreadyDistributed = await configService.AreSubmissionsDistributedAsync(roundId);

                if (alreadyDistributed)
                {
                    _logger.LogDebug("Submissions already distributed for round {RoundId}", roundId);
                    return;
                }

                _logger.LogInformation(
                    "Distributing submissions for round {RoundId} ({RoundName})",
                    round.RoundId, round.Name);

                await roundService.DistributeSubmissionsToJudgesAsync(roundId);

                _logger.LogInformation(
                    "Successfully distributed submissions for round {RoundId}",
                    roundId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to distribute submissions for round {RoundId}", roundId);
                throw;
            }
        }
    }
}
