using BusinessLogic.IServices;
using BusinessLogic.IServices.Contests;
using DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Repository.IRepositories;
using Utility.Enums;

namespace BusinessLogic.Services.Contests
{
    public class RoundSateBackgroundService : BackgroundService
    {
        private readonly ILogger<RoundSateBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5); // Check every 5 minutes

        public RoundSateBackgroundService(
            ILogger<RoundSateBackgroundService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Round State Background Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await UpdateRoundStatesAsync();
                    await CheckAndDistributeSubmissionsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while updating Round states.");
                }

                // Wait for the next check interval
                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Round State Background Service is stopping.");
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

        private async Task CheckAndDistributeSubmissionsAsync()
        {
            // Create a new scope to get scoped services
            using IServiceScope scope = _serviceProvider.CreateScope();

            IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();
            IRoundService roundService = scope.ServiceProvider.GetRequiredService<IRoundService>();
            IConfigService configService = scope.ServiceProvider.GetRequiredService<IConfigService>();

            DateTime now = DateTime.UtcNow;

            // Get all ended rounds with manual problems that are not deleted
            List<Round> endedRounds = await unitOfWork.GetRepository<Round>()
                .Entities
                .Include(r => r.Problem)
                .Where(r => !r.DeletedAt.HasValue
                    && r.End <= now
                    && r.Problem != null
                    && r.Problem.Type == ProblemTypeEnum.Manual.ToString())
                .ToListAsync();

            // Iterate through ended rounds and distribute submissions if not already done
            foreach (Round round in endedRounds)
            {
                try
                {
                    // Check if already distributed using config service
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
    }
}
