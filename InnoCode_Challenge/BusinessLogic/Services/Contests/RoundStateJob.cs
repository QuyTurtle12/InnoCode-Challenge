using BusinessLogic.IServices;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.NotificationsAndLogs;
using BusinessLogic.IServices.Contests;
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

                        var notif = scope.ServiceProvider.GetRequiredService<INotificationService>();

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

            // Schedule auto-finalize based on policy
            try
            {
                using IServiceScope scope = _serviceProvider.CreateScope();
                IRoundService roundService = scope.ServiceProvider.GetRequiredService<IRoundService>();
                IUOW uow = scope.ServiceProvider.GetRequiredService<IUOW>();

                DateTime finalizeAt = await roundService.GetFinalizeNotBeforeAsync(round.RoundId);
                DateTime finalizeAfter = finalizeAt.AddSeconds(5); // ensure auto-actions run first

                if (finalizeAfter > now)
                {
                    schedulePoints.Add((finalizeAfter, "Round Finalize"));
                }

                // Appeal review reminder (T-8h)
                DateTime reviewDeadline = await GetAppealReviewDeadlineAsync(round, uow);
                DateTime reviewReminder = reviewDeadline.AddHours(-8);
                if (reviewReminder > now)
                {
                    schedulePoints.Add((reviewReminder, "Appeal Review Reminder"));
                }

                // Auto-deny appeals exactly at review deadline
                if (reviewDeadline > now)
                {
                    schedulePoints.Add((reviewDeadline, "Appeal Auto Deny"));
                }

                // Judge reminder (T-8h before default judge deadline for manual rounds)
                DateTime? judgeDeadline = await GetDefaultJudgeDeadlineAsync(round, uow);
                if (judgeDeadline.HasValue)
                {
                    DateTime judgeReminder = judgeDeadline.Value.AddHours(-8);
                    if (judgeReminder > now)
                    {
                        schedulePoints.Add((judgeReminder, "Judge Reminder"));
                    }

                    // Auto score pending submissions at the latest allowed time
                    DateTime judgeAutoScoreAt = finalizeAt > judgeDeadline ? finalizeAt : judgeDeadline.Value;
                    if (judgeAutoScoreAt > now)
                    {
                        schedulePoints.Add((judgeAutoScoreAt, "Auto Score Pending"));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to schedule finalize time for round {RoundId}", round.RoundId);
            }

            foreach (var (time, description) in schedulePoints.OrderBy(x => x.Time))
            {
                var delay = time - now;
                if (delay.TotalSeconds > 0)
                {
                    BackgroundJob.Schedule<RoundStateJob>(
                        description switch
                        {
                            "Round Finalize" => job => job.FinalizeRoundAsync(round.RoundId),
                            "Appeal Review Reminder" => job => job.RemindAppealReviewAsync(round.RoundId),
                            "Appeal Auto Deny" => job => job.AutoDenyAppealsAsync(round.RoundId),
                            "Judge Reminder" => job => job.RemindJudgePendingAsync(round.RoundId),
                            "Auto Score Pending" => job => job.AutoScorePendingSubmissionsAsync(round.RoundId),
                            _ => job => job.UpdateSpecificRoundAsync(round.RoundId)
                        },
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
                IRoundService roundService = scope.ServiceProvider.GetRequiredService<IRoundService>();

                // Regenerate open code
                await roundService.RegenerateOpenCodeAsync(roundId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to regenerate open code for round {RoundId}", roundId);
                throw;
            }
        }

        [DisableConcurrentExecution(timeoutInSeconds: 120)]
        [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 30, 60 })]
        public async Task FinalizeRoundAsync(Guid roundId)
        {
            try
            {
                using IServiceScope scope = _serviceProvider.CreateScope();
                IRoundService roundService = scope.ServiceProvider.GetRequiredService<IRoundService>();
                await roundService.TryFinalizeRoundAsync(roundId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FinalizeRoundAsync failed for round {RoundId}", roundId);
                throw;
            }
        }

        [DisableConcurrentExecution(timeoutInSeconds: 120)]
        [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 30, 60 })]
        public async Task AutoDenyAppealsAsync(Guid roundId)
        {
            try
            {
                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW uow = scope.ServiceProvider.GetRequiredService<IUOW>();
                INotificationService notif = scope.ServiceProvider.GetRequiredService<INotificationService>();
                IActivityLogWriter logWriter = scope.ServiceProvider.GetRequiredService<IActivityLogWriter>();

                var roundRepo = uow.GetRepository<Round>();
                var appealRepo = uow.GetRepository<Appeal>();
                var mentorRepo = uow.GetRepository<Mentor>();

                Round? round = await roundRepo.Entities
                    .Include(r => r.Contest)
                    .FirstOrDefaultAsync(r => r.RoundId == roundId && r.DeletedAt == null);

                if (round == null) return;

                DateTime reviewDeadline = await GetAppealReviewDeadlineAsync(round, uow);
                if (DateTime.UtcNow < reviewDeadline) return;

                var pendingAppeals = await appealRepo.Entities
                    .Include(a => a.Team)
                    .Where(a => a.DeletedAt == null
                                && a.TargetId == roundId
                                && (a.State != AppealStateEnum.Closed.ToString()
                                    || a.Decision == AppealDecisionEnum.Pending.ToString()))
                    .ToListAsync();

                if (!pendingAppeals.Any()) return;

                Guid organizerId = Guid.Empty;
                if (round.Contest?.CreatedBy != null)
                {
                    Guid.TryParse(round.Contest.CreatedBy, out organizerId);
                }

                foreach (var appeal in pendingAppeals)
                {
                    appeal.State = AppealStateEnum.Closed.ToString();
                    appeal.Decision = AppealDecisionEnum.Rejected.ToString();
                    if (string.IsNullOrWhiteSpace(appeal.DecisionReason))
                    {
                        appeal.DecisionReason = "Auto-denied because the appeal review deadline passed.";
                    }

                    await appealRepo.UpdateAsync(appeal);
                }

                await uow.SaveAsync();

                foreach (var appeal in pendingAppeals)
                {
                    var recipients = new HashSet<Guid>();
                    if (appeal.OwnerId != Guid.Empty) recipients.Add(appeal.OwnerId);

                    if (appeal.Team?.MentorId != null)
                    {
                        Guid? mentorUserId = await mentorRepo.Entities
                            .Where(m => m.MentorId == appeal.Team.MentorId && m.DeletedAt == null)
                            .Select(m => (Guid?)m.UserId)
                            .FirstOrDefaultAsync();
                        if (mentorUserId.HasValue) recipients.Add(mentorUserId.Value);
                    }

                    if (organizerId != Guid.Empty) recipients.Add(organizerId);

                    if (recipients.Count > 0)
                    {
                        await notif.CreateInAppToUsersAsync(
                            recipients,
                            NotificationTypes.AppealUpdated,
                            new
                            {
                                appealId = appeal.AppealId,
                                teamId = appeal.TeamId,
                                roundId = appeal.TargetId,
                                contestId = round.ContestId,
                                state = appeal.State,
                                decision = appeal.Decision,
                                targetType = TargetTypes.Appeal,
                                targetId = appeal.AppealId.ToString(),
                                message = "Appeal was auto-denied after the review deadline."
                            });
                    }

                    if (organizerId != Guid.Empty)
                    {
                        await logWriter.TryWriteAsync(
                            organizerId,
                            ActivityActions.AppealResolve,
                            TargetTypes.Appeal,
                            appeal.AppealId.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoDenyAppealsAsync failed for round {RoundId}", roundId);
                throw;
            }
        }

        [DisableConcurrentExecution(timeoutInSeconds: 120)]
        [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 30, 60 })]
        public async Task AutoScorePendingSubmissionsAsync(Guid roundId)
        {
            try
            {
                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW uow = scope.ServiceProvider.GetRequiredService<IUOW>();
                INotificationService notif = scope.ServiceProvider.GetRequiredService<INotificationService>();
                ILeaderboardEntryService leaderboardService = scope.ServiceProvider.GetRequiredService<ILeaderboardEntryService>();
                IActivityLogWriter logWriter = scope.ServiceProvider.GetRequiredService<IActivityLogWriter>();

                var roundRepo = uow.GetRepository<Round>();
                var submissionRepo = uow.GetRepository<Submission>();
                var configRepo = uow.GetRepository<Config>();
                var contestRepo = uow.GetRepository<Contest>();

                Round? round = await roundRepo.Entities
                    .Include(r => r.Problem)
                    .FirstOrDefaultAsync(r => r.RoundId == roundId && r.DeletedAt == null);

                if (round == null || round.Problem?.Type != ProblemTypeEnum.Manual.ToString())
                    return;

                int judgeDays = await GetPolicyDaysAsync(configRepo, round.ContestId, ContestPolicyKeys.JudgeRescoreDays, 1);
                DateTime defaultDeadline = round.End.AddDays(judgeDays);

                var pendingSubs = await submissionRepo.Entities
                    .Include(s => s.Problem)
                    .Where(s => s.DeletedAt == null
                                && s.Problem != null
                                && s.Problem.RoundId == roundId
                                && s.Status == SubmissionStatusEnum.Pending.ToString())
                    .ToListAsync();

                if (!pendingSubs.Any()) return;

                Guid organizerId = Guid.Empty;
                string? organizerStr = await contestRepo.Entities
                    .Where(c => c.ContestId == round.ContestId && c.DeletedAt == null)
                    .Select(c => c.CreatedBy)
                    .FirstOrDefaultAsync();
                Guid.TryParse(organizerStr, out organizerId);

                var overdue = new List<Submission>();
                foreach (var sub in pendingSubs)
                {
                    DateTime deadline = defaultDeadline;
                    if (!string.IsNullOrWhiteSpace(sub.JudgedBy) && Guid.TryParse(sub.JudgedBy, out Guid judgeId))
                    {
                        string key = ConfigKeys.JudgeSubmissionDeadline(judgeId, sub.SubmissionId);
                        string? val = await configRepo.Entities
                            .Where(c => c.Key == key && c.DeletedAt == null)
                            .Select(c => c.Value)
                            .FirstOrDefaultAsync();
                        if (DateTime.TryParse(val, out DateTime parsed))
                        {
                            deadline = parsed;
                        }
                    }

                    if (DateTime.UtcNow <= deadline) continue;

                    sub.Status = SubmissionStatusEnum.Finished.ToString();
                    sub.Score = 0;
                    await submissionRepo.UpdateAsync(sub);
                    overdue.Add(sub);
                }

                if (!overdue.Any()) return;

                await uow.SaveAsync();

                foreach (var sub in overdue)
                {
                    await leaderboardService.UpdateTeamScoreAsync(round.ContestId, sub.TeamId);

                    var recipients = new HashSet<Guid>();
                    if (!string.IsNullOrWhiteSpace(sub.JudgedBy) && Guid.TryParse(sub.JudgedBy, out Guid judgeId))
                    {
                        recipients.Add(judgeId);
                    }
                    if (organizerId != Guid.Empty) recipients.Add(organizerId);

                    if (recipients.Count > 0)
                    {
                        await notif.CreateInAppToUsersAsync(
                            recipients,
                            NotificationTypes.SubmissionStatusChanged,
                            new
                            {
                                contestId = round.ContestId,
                                roundId = round.RoundId,
                                submissionId = sub.SubmissionId,
                                status = sub.Status,
                                message = "Submission was auto-scored 0 because the judge deadline passed."
                            });
                    }

                    if (organizerId != Guid.Empty)
                    {
                        await logWriter.TryWriteAsync(
                            organizerId,
                            ActivityActions.SubmissionStatusChange,
                            TargetTypes.Submission,
                            sub.SubmissionId.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoScorePendingSubmissionsAsync failed for round {RoundId}", roundId);
                throw;
            }
        }

        [DisableConcurrentExecution(timeoutInSeconds: 120)]
        [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 30, 60 })]
        public async Task RemindAppealReviewAsync(Guid roundId)
        {
            try
            {
                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW uow = scope.ServiceProvider.GetRequiredService<IUOW>();
                INotificationService notif = scope.ServiceProvider.GetRequiredService<INotificationService>();

                IGenericRepository<Round> roundRepo = uow.GetRepository<Round>();
                IGenericRepository<Appeal> appealRepo = uow.GetRepository<Appeal>();
                IGenericRepository<Contest> contestRepo = uow.GetRepository<Contest>();

                Round? round = await roundRepo.Entities.FirstOrDefaultAsync(r => r.RoundId == roundId && r.DeletedAt == null);
                if (round == null) return;

                DateTime deadline = await GetAppealReviewDeadlineAsync(round, uow);
                DateTime now = DateTime.UtcNow;
                if (now < deadline.AddHours(-8) || now > deadline) return;

                bool hasOpenAppeals = await appealRepo.Entities.AsNoTracking()
                    .AnyAsync(a => a.DeletedAt == null
                                   && a.TargetId == roundId
                                   && (a.State != AppealStateEnum.Closed.ToString()
                                       || a.Decision == AppealDecisionEnum.Pending.ToString()));
                if (!hasOpenAppeals) return;

                string? organizerStr = await contestRepo.Entities
                    .Where(c => c.ContestId == round.ContestId && c.DeletedAt == null)
                    .Select(c => c.CreatedBy)
                    .FirstOrDefaultAsync();

                Guid organizerId;
                bool parsedOrganizer = Guid.TryParse(organizerStr, out organizerId);

                if (parsedOrganizer && organizerId != Guid.Empty)
                {
                    await notif.CreateInAppToUserAsync(
                        organizerId,
                        NotificationTypes.RoundUpdated,
                        new
                        {
                            contestId = round.ContestId,
                            roundId = round.RoundId,
                            message = "Appeal review deadline is approaching. There are pending appeals to review."
                        });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RemindAppealReviewAsync failed for round {RoundId}", roundId);
                throw;
            }
        }

        [DisableConcurrentExecution(timeoutInSeconds: 120)]
        [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 30, 60 })]
        public async Task RemindJudgePendingAsync(Guid roundId)
        {
            try
            {
                using IServiceScope scope = _serviceProvider.CreateScope();
                IUOW uow = scope.ServiceProvider.GetRequiredService<IUOW>();
                INotificationService notif = scope.ServiceProvider.GetRequiredService<INotificationService>();

                var roundRepo = uow.GetRepository<Round>();
                var submissionRepo = uow.GetRepository<Submission>();
                var configRepo = uow.GetRepository<Config>();

                Round? round = await roundRepo.Entities
                    .Include(r => r.Problem)
                    .FirstOrDefaultAsync(r => r.RoundId == roundId && r.DeletedAt == null);
                if (round == null || round.Problem?.Type != ProblemTypeEnum.Manual.ToString()) return;

                DateTime defaultDeadline = await GetDefaultJudgeDeadlineAsync(round, uow) ?? DateTime.UtcNow;

                var pendingSubs = await submissionRepo.Entities
                    .Where(s => s.DeletedAt == null
                                && s.Problem != null
                                && s.Problem.RoundId == roundId
                                && s.Status == SubmissionStatusEnum.Pending.ToString()
                                && !string.IsNullOrWhiteSpace(s.JudgedBy))
                    .ToListAsync();

                if (!pendingSubs.Any()) return;

                DateTime now = DateTime.UtcNow;
                var grouped = pendingSubs.GroupBy(s => s.JudgedBy);

                foreach (var group in grouped)
                {
                    if (!Guid.TryParse(group.Key, out Guid judgeId)) continue;

                    // Determine the earliest deadline among this judge's pending submissions
                    DateTime minDeadline = defaultDeadline;
                    foreach (var sub in group)
                    {
                        string key = ConfigKeys.JudgeSubmissionDeadline(judgeId, sub.SubmissionId);
                        string? val = await configRepo.Entities
                            .Where(c => c.Key == key && c.DeletedAt == null)
                            .Select(c => c.Value)
                            .FirstOrDefaultAsync();

                        if (DateTime.TryParse(val, out DateTime parsed))
                        {
                            if (parsed < minDeadline) minDeadline = parsed;
                        }
                    }

                    if (now >= minDeadline.AddHours(-8) && now <= minDeadline)
                    {
                        await notif.CreateInAppToUserAsync(
                            judgeId,
                            NotificationTypes.ManualGradingAssigned,
                            new
                            {
                                contestId = round.ContestId,
                                roundId = round.RoundId,
                                message = "Manual grading deadline is approaching. You have pending submissions."
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RemindJudgePendingAsync failed for round {RoundId}", roundId);
                throw;
            }
        }

        private async Task<DateTime> GetAppealReviewDeadlineAsync(Round round, IUOW uow)
        {
            var configRepo = uow.GetRepository<Config>();
            string key = ConfigKeys.RoundAppealReviewDeadlineUtc(round.RoundId);

            string? val = await configRepo.Entities
                .Where(c => c.Key == key && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (DateTime.TryParse(val, out DateTime parsed))
                return parsed;

            int submitDays = await GetPolicyDaysAsync(configRepo, round.ContestId, ContestPolicyKeys.AppealSubmitDays, 2);
            int reviewDays = await GetPolicyDaysAsync(configRepo, round.ContestId, ContestPolicyKeys.AppealReviewDays, 1);
            return round.End.AddDays(submitDays + reviewDays);
        }

        private async Task<DateTime?> GetDefaultJudgeDeadlineAsync(Round round, IUOW uow)
        {
            if (round.Problem?.Type != ProblemTypeEnum.Manual.ToString())
                return null;

            var configRepo = uow.GetRepository<Config>();
            int judgeDays = await GetPolicyDaysAsync(configRepo, round.ContestId, ContestPolicyKeys.JudgeRescoreDays, 1);
            return round.End.AddDays(judgeDays);
        }

        private static async Task<int> GetPolicyDaysAsync(IGenericRepository<Config> configRepo, Guid contestId, string key, int defaultValue)
        {
            string policyKey = ConfigKeys.ContestPolicy(contestId, key);
            string? val = await configRepo.Entities
                .Where(c => c.Key == policyKey && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            return int.TryParse(val, out int days) && days >= 0 ? days : defaultValue;
        }
    }
}
