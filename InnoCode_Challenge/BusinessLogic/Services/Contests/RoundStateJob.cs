using BusinessLogic.IServices;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.NotificationsAndLogs;
using DataAccess.Entities;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Repository.IRepositories;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Microsoft.AspNetCore.Http;

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

        [DisableConcurrentExecution(timeoutInSeconds: 60)]
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 10, 30, 60 })]
        public async Task UpdateSpecificRoundAsync(Guid roundId)
        {
            try
            {
                _logger.LogInformation("Processing round state update for {RoundId} at {Time}",
                    roundId, DateTime.UtcNow);

                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();
                IRoundService roundService = scope.ServiceProvider.GetRequiredService<IRoundService>();

                IGenericRepository<Round> roundRepo = unitOfWork.GetRepository<Round>();
                DateTime now = DateTime.UtcNow;

                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId && r.DeletedAt == null)
                    .Include(r => r.Contest)
                    .FirstOrDefaultAsync();

                if (round == null)
                {
                    _logger.LogWarning("Round {RoundId} not found or deleted", roundId);
                    return;
                }

                string? newStatus = DetermineRoundStatus(round, now);

                if (newStatus != null && newStatus != round.Status)
                {
                    // Guard: when opening a round, ensure previous main round is finalized
                    if (newStatus == RoundStatusEnum.Opened.ToString())
                    {
                        await EnsurePreviousRoundFinalizedAsync(round, unitOfWork);
                    }

                    string oldStatus = round.Status ?? "null";
                    round.Status = newStatus;
                    await roundRepo.UpdateAsync(round);
                    await unitOfWork.SaveAsync();
                    _logger.LogInformation(
                        "Round {RoundId} ({RoundName}) status changed from {OldStatus} to {NewStatus}",
                        round.RoundId, round.Name, oldStatus, newStatus);

                    try
                    {

                        var notif = scope.ServiceProvider.GetRequiredService<INotificationService>(); // [NEW]

                        async Task<List<Guid>> GetParticipantIdsAsync()
                        {
                            var teamRepo = unitOfWork.GetRepository<Team>();

                            var studentIds = await teamRepo.Entities.AsNoTracking()
                                .Where(t => t.ContestId == round.ContestId && t.DeletedAt == null)
                                .SelectMany(t => t.TeamMembers.Select(tm => tm.Student.UserId))
                                .ToListAsync();

                            var mentorIds = await teamRepo.Entities.AsNoTracking()
                                .Where(t => t.ContestId == round.ContestId && t.DeletedAt == null && t.MentorId != Guid.Empty)
                                .Select(t => t.Mentor.UserId)
                                .ToListAsync();

                            return studentIds.Concat(mentorIds).Where(x => x != Guid.Empty).Distinct().ToList();
                        }

                        var participantIds = await GetParticipantIdsAsync();
                        if (participantIds.Count > 0)
                        {
                            if (newStatus == RoundStatusEnum.Opened.ToString())
                            {
                                await notif.CreateInAppToUsersAsync(participantIds, NotificationTypes.RoundStarted, new
                                {
                                    contestId = round.ContestId,
                                    roundId = round.RoundId,
                                    name = round.Name,
                                    targetType = TargetTypes.Round,
                                    targetId = round.RoundId.ToString(),
                                    message = $"Round '{round.Name}' has started."
                                });
                            }

                            if (newStatus == RoundStatusEnum.Closed.ToString())
                            {
                                await notif.CreateInAppToUsersAsync(participantIds, NotificationTypes.RoundEnded, new
                                {
                                    contestId = round.ContestId,
                                    roundId = round.RoundId,
                                    name = round.Name,
                                    targetType = TargetTypes.Round,
                                    targetId = round.RoundId.ToString(),
                                    message = $"Round '{round.Name}' has ended."
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Notify failed for round {RoundId}", roundId);
                    }

                    try
                    {
                        // Schedule next state transition
                        await ScheduleNextStateTransitionAsync(round);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Scheduling next transition failed for round {RoundId}", roundId);
                    }

                    // Generate open code if round just opened
                    if (newStatus == RoundStatusEnum.Opened.ToString())
                    {
                        try
                        {
                            // Generate initial open code
                            await roundService.GenerateOpenCode(round.RoundId);
                            _logger.LogInformation(
                                "Successfully generated initial open code for round {RoundId} ({RoundName})",
                                round.RoundId, round.Name);

                            // Start recurring job to regenerate every minute
                            string recurringJobId = $"regenerate-open-code-{roundId}";
                            RecurringJob.AddOrUpdate<RoundStateJob>(
                                recurringJobId,
                                job => job.RegenerateOpenCodeAsync(roundId),
                                "*/1 * * * *", // Every 1 minute
                                new RecurringJobOptions
                                {
                                    TimeZone = TimeZoneInfo.Utc
                                });

                            _logger.LogInformation(
                                "Started recurring open code generation for round {RoundId} (every 1 minute)",
                                roundId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Failed to setup open code generation for round {RoundId} ({RoundName})",
                                round.RoundId, round.Name);
                        }
                    }

                    // When round closes, stop recurring job and trigger submission distribution
                    if (newStatus == RoundStatusEnum.Closed.ToString())
                    {
                        // Stop the recurring open code generation job
                        string recurringJobId = $"regenerate-open-code-{roundId}";
                        RecurringJob.RemoveIfExists(recurringJobId);
                        _logger.LogInformation(
                            "Stopped recurring open code generation for round {RoundId}",
                            roundId);

                        // Trigger submission distribution
                        BackgroundJob.Enqueue<RoundStateJob>(job =>
                            job.CheckAndDistributeSubmissionsForRoundAsync(roundId));
                    }
                }
                else
                {
                    _logger.LogDebug("Round {RoundId} status unchanged ({Status})",
                        roundId, round.Status ?? "null");
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
            if (now < round.Start)
            {
                return RoundStatusEnum.Incoming.ToString();
            }

            if (now >= round.Start && now < round.End)
            {
                return RoundStatusEnum.Opened.ToString();
            }

            if (now >= round.End)
            {
                return RoundStatusEnum.Closed.ToString();
            }

            return null;
        }

        private async Task EnsurePreviousRoundFinalizedAsync(Round round, IUOW unitOfWork)
        {
            IGenericRepository<Round> roundRepo = unitOfWork.GetRepository<Round>();
            IGenericRepository<Submission> submissionRepo = unitOfWork.GetRepository<Submission>();
            IGenericRepository<Appeal> appealRepo = unitOfWork.GetRepository<Appeal>();

            Round? prevRound = await roundRepo.Entities
                .AsNoTracking()
                .Where(r => r.ContestId == round.ContestId
                            && !r.IsRetakeRound
                            && r.RoundId != round.RoundId
                            && r.End <= round.Start
                            && r.DeletedAt == null)
                .OrderByDescending(r => r.End)
                .FirstOrDefaultAsync();

            if (prevRound == null) return;

            bool hasUnfinishedSubmissions = await submissionRepo.Entities
                .AsNoTracking()
                .AnyAsync(s =>
                    s.DeletedAt == null
                    && s.Problem != null
                    && s.Problem.RoundId == prevRound.RoundId
                    && s.Status == SubmissionStatusEnum.Pending.ToString());

            bool hasPendingAppeals = await appealRepo.Entities
                .AsNoTracking()
                .AnyAsync(a =>
                    a.DeletedAt == null
                    && a.TargetId == prevRound.RoundId
                    && (a.State != AppealStateEnum.Closed.ToString() || a.Decision == AppealDecisionEnum.Pending.ToString()));

            if (hasUnfinishedSubmissions || hasPendingAppeals)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    $"Cannot open round '{round.Name}' because previous round '{prevRound.Name}' is not finalized.");
            }
        }

        private async Task ScheduleNextStateTransitionAsync(Round round)
        {
            DateTime now = DateTime.UtcNow;
            var schedulePoints = new List<(DateTime Time, string Description)>();

            if (round.Start > now)
            {
                schedulePoints.Add((round.Start, "Round Start (Opened)"));
            }

            if (round.End > now)
            {
                schedulePoints.Add((round.End, "Round End (Closed)"));
            }

            foreach (var (time, description) in schedulePoints.OrderBy(x => x.Time))
            {
                var delay = time - now;
                if (delay.TotalSeconds > 0)
                {
                    BackgroundJob.Schedule<RoundStateJob>(
                        job => job.UpdateSpecificRoundAsync(round.RoundId),
                        delay);

                    _logger.LogInformation(
                        "Scheduled '{Description}' for round {RoundId} at {Time} (in {Minutes:F2} minutes)",
                        description, round.RoundId, time, delay.TotalMinutes);
                }
            }
        }

        [DisableConcurrentExecution(timeoutInSeconds: 120)]
        [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 30, 60 })]
        public async Task CheckAndDistributeSubmissionsForRoundAsync(Guid roundId)
        {
            try
            {
                using IServiceScope scope = _serviceProvider.CreateScope();

                IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();
                IRoundService roundService = scope.ServiceProvider.GetRequiredService<IRoundService>();
                IConfigService configService = scope.ServiceProvider.GetRequiredService<IConfigService>();

                bool alreadyDistributed = await configService.AreSubmissionsDistributedAsync(roundId);

                if (alreadyDistributed)
                {
                    _logger.LogDebug("Submissions already distributed for round {RoundId}, skipping", roundId);
                    return;
                }

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

                if (round.Problem?.Type != ProblemTypeEnum.Manual.ToString())
                {
                    _logger.LogDebug("Round {RoundId} does not have manual problem type, skipping distribution", roundId);
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

        [DisableConcurrentExecution(timeoutInSeconds: 30)]
        [AutomaticRetry(Attempts = 2)]
        public async Task RegenerateOpenCodeAsync(Guid roundId)
        {
            try
            {
                _logger.LogDebug("Regenerating open code for round {RoundId} at {Time}",
                    roundId, DateTime.UtcNow);

                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW unitOfWork = scope.ServiceProvider.GetRequiredService<IUOW>();
                IRoundService roundService = scope.ServiceProvider.GetRequiredService<IRoundService>();

                IGenericRepository<Round> roundRepo = unitOfWork.GetRepository<Round>();

                // Check if round is still in Opened status
                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == roundId && r.DeletedAt == null)
                    .FirstOrDefaultAsync();

                if (round == null)
                {
                    _logger.LogWarning("Round {RoundId} not found, stopping open code generation", roundId);

                    // Remove the recurring job
                    string recurringJobId = $"regenerate-open-code-{roundId}";
                    RecurringJob.RemoveIfExists(recurringJobId);
                    return;
                }

                // Only regenerate if round is still open
                if (round.Status != RoundStatusEnum.Opened.ToString())
                {
                    _logger.LogInformation(
                        "Round {RoundId} is no longer open (status: {Status}), stopping open code generation",
                        roundId, round.Status);

                    // Remove the recurring job
                    string recurringJobId = $"regenerate-open-code-{roundId}";
                    RecurringJob.RemoveIfExists(recurringJobId);
                    return;
                }

                // Check if round has ended
                DateTime now = DateTime.UtcNow;
                if (now >= round.End)
                {
                    _logger.LogInformation(
                        "Round {RoundId} has ended, stopping open code generation",
                        roundId);

                    // Remove the recurring job
                    string recurringJobId = $"regenerate-open-code-{roundId}";
                    RecurringJob.RemoveIfExists(recurringJobId);
                    return;
                }

                // Regenerate the open code
                await roundService.GenerateOpenCode(round.RoundId);

                _logger.LogDebug("Successfully regenerated open code for round {RoundId}", roundId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to regenerate open code for round {RoundId}", roundId);
                throw;
            }
        }
    }
}
