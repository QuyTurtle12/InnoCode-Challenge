using AutoMapper;
using BusinessLogic.IServices;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.FileStorages;
using BusinessLogic.IServices.Mcqs;
using BusinessLogic.IServices.NotificationsAndLogs;
using BusinessLogic.Helpers;
using DataAccess.Entities;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repository.DTOs.ContestDTOs;
using Repository.DTOs.McqTestDTOs;
using Repository.DTOs.ProblemDTOs;
using Repository.DTOs.RoundDTOs;
using Repository.IRepositories;
using System.Globalization;
using System.Security.Claims;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.Helpers;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Contests
{
    public class RoundService : IRoundService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private readonly IMcqTestService _mcqTestService;
        private readonly IProblemService _problemService;
        private readonly IContestJudgeService _contestJudgeService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfigService _configService;
        private readonly ICloudinaryService _cloudinaryService;

        private readonly INotificationService _notificationService;   
        private readonly IActivityLogWriter _activityLogWriter;       
        private readonly ILogger<RoundService> _logger;

        private const string CODE_TEMPLATE_FOLDER = "code_template";
        private const string SCOPE_CONTEST = "contest";
        private const string SCOPE_ROUND = "round";
        private const string JUDGE_STATUS_ACTIVE = "active";
        private const int OPEN_CODE_MIN = 1000;
        private const int OPEN_CODE_MAX = 10000;
        private const int DEFAULT_APPEAL_SUBMIT_DAYS = 2;
        private const int DEFAULT_APPEAL_REVIEW_DAYS = 1;
        private const int DEFAULT_JUDGE_RESCORE_DAYS = 1;

        // Round status enum values
        private static readonly string ROUND_STATUS_INCOMING = RoundStatusEnum.Incoming.ToString();
        private static readonly string ROUND_STATUS_OPENED = RoundStatusEnum.Opened.ToString();
        private static readonly string ROUND_STATUS_CLOSED = RoundStatusEnum.Closed.ToString();

        // Submission and appeal status values
        private static readonly string SUBMISSION_STATUS_PENDING = SubmissionStatusEnum.Pending.ToString();
        private static readonly string SUBMISSION_STATUS_FINISHED = SubmissionStatusEnum.Finished.ToString();
        private static readonly string APPEAL_STATE_CLOSED = AppealStateEnum.Closed.ToString();
        private static readonly string APPEAL_DECISION_APPROVED = AppealDecisionEnum.Approved.ToString();
        private static readonly string APPEAL_RESOLUTION_RETAKE = AppealResolutionEnum.Retake.ToString();

        // Auto test types values
        private const string AUTO_MOCK_TEST_TEST_TYPE = nameof(TestTypeEnum.MockTest);
        private const string AUTO_INPUT_OUTPUT_TEST_TYPE = nameof(TestTypeEnum.InputOutput);

        public RoundService(
            IMapper mapper,
            IUOW unitOfWork,
            IMcqTestService mcqTestService,
            IProblemService problemService,
            IContestJudgeService contestJudgeService,
            IHttpContextAccessor httpContextAccessor,
            IConfigService configService,
            ICloudinaryService cloudinaryService,
            INotificationService notificationService,               
            IActivityLogWriter activityLogWriter,                     
            ILogger<RoundService> logger)                             
        {

            _mapper = mapper;
            _unitOfWork = unitOfWork;
            _mcqTestService = mcqTestService;
            _problemService = problemService;
            _contestJudgeService = contestJudgeService;
            _httpContextAccessor = httpContextAccessor;
            _configService = configService;
            _cloudinaryService = cloudinaryService;
            _notificationService = notificationService;               
            _activityLogWriter = activityLogWriter;                   
            _logger = logger;                                         
        }

        public async Task CreateRoundAsync(Guid contestId, CreateRoundDTO roundDTO)
        {
            bool committed = false;
            Round? createdRound = null;

            try
            {
                _unitOfWork.BeginTransaction();

                // Validate all input parameters
                ValidateCreateRoundInput(roundDTO);

                // Validate retake round configuration
                await ValidateRetakeRoundAsync(contestId, roundDTO.MainRoundId, roundDTO.IsRetakeRound, roundDTO.ProblemType);

                // Validate round dates and conflicts
                await ValidateRoundInputAsync(contestId, roundDTO, null);

                // Get repositories
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Create round entity
                Round round = CreateRoundEntity(contestId, roundDTO);

                // Insert new round
                await roundRepo.InsertAsync(round);
                await _unitOfWork.SaveAsync();

                // Configure round settings (time limit, rank cutoff)
                await ConfigureRoundSettingsAsync(round.RoundId, contestId, round.End, roundDTO, configRepo);

                // Create problem or MCQ test based on type
                await CreateRoundContentAsync(round.RoundId, roundDTO);

                // Save all changes
                await _unitOfWork.SaveAsync();
                _unitOfWork.CommitTransaction();

                committed = true;
                createdRound = round;
            }
            catch (Exception ex)
            {
                if (!committed) _unitOfWork.RollBack();
                if (ex is ErrorException) throw;
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating Rounds: {ex.Message}");
            }

            // Post-creation operations (logging, scheduling)
            await PerformPostCreateRoundOperationsAsync(createdRound!);
        }

        private async Task ValidateRetakeRoundAsync(Guid contestId, Guid? mainRoundId, bool isRetakeRound, ProblemTypeEnum? retakeRoundType = null)
        {
            if (!isRetakeRound)
            {
                return;
            }

            // Retake round must have a main round reference
            if (!mainRoundId.HasValue || mainRoundId.Value == Guid.Empty)
            {
                throw new ErrorException(
                    StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Retake round must reference a valid main round ID."
                );
            }

            // Validate main round exists and belongs to same contest
            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
            Round? mainRound = await roundRepo.Entities
                .Where(r => r.RoundId == mainRoundId.Value
                    && r.ContestId == contestId
                    && !r.DeletedAt.HasValue)
                .Include(r => r.Problem)
                .Include(r => r.McqTest)
                .FirstOrDefaultAsync();

            if (mainRound == null)
            {
                throw new ErrorException(
                    StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    "Main round not found or does not belong to this contest."
                );
            }

            // Main round cannot itself be a retake round
            if (mainRound.IsRetakeRound)
            {
                throw new ErrorException(
                    StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Cannot create a retake round for another retake round."
                );
            }

            // Validate that retake round has the same type as main round
            if (retakeRoundType.HasValue)
            {
                string? mainRoundType = null;

                if (mainRound.McqTest != null && mainRound.McqTest.DeletedAt == null)
                {
                    mainRoundType = ProblemTypeEnum.McqTest.ToString();
                }
                else if (mainRound.Problem != null && mainRound.Problem.DeletedAt == null)
                {
                    mainRoundType = mainRound.Problem.Type;
                }

                if (string.IsNullOrEmpty(mainRoundType))
                {
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Main round does not have a valid problem type."
                    );
                }

                if (!string.Equals(retakeRoundType.ToString(), mainRoundType, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        $"Retake round type ({retakeRoundType}) must match main round type ({mainRoundType})."
                    );
                }
            }
        }

        public async Task DeleteRoundAsync(Guid id)
        {
            bool committed = false;
            Guid contestId = Guid.Empty;
            string roundName = string.Empty;
            Guid roundId = id;

            try
            {
                _unitOfWork.BeginTransaction();

                // Validate input
                ValidateRoundId(id);

                // Get repositories
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

                // Find round with related entities
                Round round = await GetRoundForDeletionAsync(roundRepo, id);

                // Delete related content
                await DeleteRoundContentAsync(round);

                // Delete round configurations
                await DeleteRoundConfigurationsAsync(round.RoundId);

                // Soft delete the round
                round.DeletedAt = DateTime.UtcNow;
                await roundRepo.UpdateAsync(round);

                // Save changes
                await _unitOfWork.SaveAsync();
                _unitOfWork.CommitTransaction();

                committed = true;
                contestId = round.ContestId;
                roundName = round.Name;
            }
            catch (Exception ex)
            {
                if (!committed) _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error deleting Round: {ex.Message}");
            }

            // Post-deletion operations
            await PerformPostDeleteRoundOperationsAsync(roundId, contestId, roundName);
        }

        private async Task<bool> HasApprovedRetakeAppealAsync(Guid studentUserId, Guid mainRoundId)
        {
            IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();

            bool hasApprovedRetake = await appealRepo.Entities
                .AnyAsync(a => a.OwnerId == studentUserId
                    && a.TargetId == mainRoundId
                    && a.State == AppealStateEnum.Closed.ToString()
                    && a.Decision == AppealDecisionEnum.Approved.ToString()
                    && a.AppealResolution == AppealResolutionEnum.Retake.ToString()
                    && a.DeletedAt == null);

            return hasApprovedRetake;
        }

        public async Task<GetRoundDTO> GetRoundByIdAsync(Guid id, string? openCode)
        {
            try
            {
                // Validate input
                ValidateRoundId(id);

                // Get round with related entities
                Round round = await FetchRoundWithIncludesAsync(id);

                // Load configurations
                var (timeLimitSeconds, rankCutoff, mockTestRoundWeight) = await LoadRoundConfigurationsAsync(id);

                // Map to DTO
                GetRoundDTO roundDTO = MapRoundToDTO(round, timeLimitSeconds, rankCutoff, mockTestRoundWeight);

                // Apply student-specific validations if user is a student
                await ApplyStudentValidationsAsync(round, openCode);

                return roundDTO;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving Round: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetRoundDTO>> GetPaginatedRoundAsync(
            int pageNumber,
            int pageSize,
            Guid? idSearch,
            Guid? contestIdSearch,
            string? roundNameSearch,
            string? contestNameSearch,
            DateTime? startDate,
            DateTime? endDate)
        {
            try
            {
                // Validate pagination parameters
                ValidatePaginationParameters(pageNumber, pageSize);

                // Get Round Repository
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

                // Build base query
                IQueryable<Round> query = BuildBaseRoundQuery(roundRepo);

                // Apply search filters
                query = ApplyRoundSearchFilters(query, idSearch, contestIdSearch, roundNameSearch, contestNameSearch, startDate, endDate);

                // Get paginated results
                PaginatedList<Round> resultQuery = await roundRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Load time limit configurations
                Dictionary<string, Config> timeLimitLookup = await LoadTimeLimitConfigurationsAsync(resultQuery.Items);

                // Load mock test weight configurations
                Dictionary<string, Config> mockTestWeightLookup = await LoadMockTestWeightConfigurationsAsync(resultQuery.Items);

                // Map to DTOs
                IReadOnlyCollection<GetRoundDTO> result = MapRoundsToDTO(resultQuery.Items, timeLimitLookup, mockTestWeightLookup);

                // Return paginated result
                return new PaginatedList<GetRoundDTO>(
                    result,
                    resultQuery.TotalCount,
                    resultQuery.PageNumber,
                    resultQuery.PageSize
                );
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving Rounds: {ex.Message}");
            }
        }

        public async Task UpdateRoundAsync(Guid id, UpdateRoundDTO roundDTO)
        {
            bool committed = false;

            try
            {
                _unitOfWork.BeginTransaction();

                // Validate input
                ValidateUpdateRoundInput(id, roundDTO);

                // Get repositories
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Find and validate round
                Round round = await GetExistingRoundOrThrowAsync(roundRepo, id);

                // Prevent updates while round is opened
                if (round.Status == ROUND_STATUS_OPENED)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Cannot update round while it is in 'Opened' status.");
                }

                // Validate against contest dates and other rounds
                await ValidateRoundInputAsync(round.ContestId, roundDTO, round.RoundId);

                // Update round entity
                await UpdateRoundEntityAsync(round, roundDTO, configRepo);

                // Update the round
                await roundRepo.UpdateAsync(round);
                await _unitOfWork.SaveAsync();

                _unitOfWork.CommitTransaction();
                committed = true;

                // Post-update operations
                await PerformPostUpdateRoundOperationsAsync(round);
            }
            catch (Exception ex)
            {
                if (!committed) _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating Round: {ex.Message}");
            }
        }

        private async Task ValidateRoundInputAsync(Guid contestId, BaseRoundDTO roundDTO, Guid? excludeRoundId)
        {
            // Get Contest Repository
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            // Fetch the contest
            Contest? contest = await contestRepo.GetByIdAsync(contestId);

            // Check if contest exists
            if (contest == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");
            }

            // Validate date range
            if (roundDTO.Start > roundDTO.End)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Start date cannot be later than end date.");
            }

            // Validate round dates are within contest dates
            if (contest.Start.HasValue && roundDTO.Start < contest.Start.Value)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    $"Round start date cannot be before contest start date ({DateTimeHelpers.ToIso8601String(contest.Start.Value)}).");
            }

            if (contest.End.HasValue && roundDTO.End > contest.End.Value)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    $"Round end date cannot be after contest end date ({DateTimeHelpers.ToIso8601String(contest.End.Value)}).");
            }

            // Validate time limit seconds
            if (roundDTO.TimeLimitSeconds.HasValue && roundDTO.TimeLimitSeconds.Value < 0)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    "Time limit seconds cannot be negative.");
            }

            // Validate time limit does not exceed round duration
            if (roundDTO.TimeLimitSeconds.HasValue && roundDTO.TimeLimitSeconds.Value > 0)
            {
                TimeSpan roundDuration = roundDTO.End - roundDTO.Start;
                if (roundDTO.TimeLimitSeconds.Value > roundDuration.TotalSeconds)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                        "Time limit seconds cannot exceed the total duration of the round.");
                }
            }

            // Get Round Repository
            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
            Round? currentRound = null;

            if (excludeRoundId.HasValue)
            {
                currentRound = await roundRepo.Entities
                    .Include(r => r.Problem)
                    .Include(r => r.McqTest)
                    .FirstOrDefaultAsync(r => r.RoundId == excludeRoundId.Value);
            }

            // Get all rounds for this contest (excluding the current round if updating)
            IQueryable<Round> existingRoundsQuery = roundRepo.Entities
                .Where(r => r.ContestId == contestId && !r.DeletedAt.HasValue)
                .Include(r => r.Problem)
                .Include(r => r.McqTest);

            if (excludeRoundId.HasValue)
            {
                existingRoundsQuery = existingRoundsQuery.Where(r => r.RoundId != excludeRoundId.Value);
            }

            List<Round> existingRounds = await existingRoundsQuery.ToListAsync();

            // Check for date conflicts with existing rounds
            foreach (Round existingRound in existingRounds)
            {
                // Check if dates overlap
                if (roundDTO.Start <= existingRound.End && roundDTO.End >= existingRound.Start)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                        $"Round dates conflict with existing round '{existingRound.Name}' ({DateTimeHelpers.ToIso8601String(existingRound.Start)} - {DateTimeHelpers.ToIso8601String(existingRound.End)}).");
                }
            }

            // Enforce buffer and finalization with the most recent previous round
            Round? previousRound = existingRounds
                .Where(r => r.End <= roundDTO.Start)
                .OrderByDescending(r => r.End)
                .FirstOrDefault();

            if (previousRound != null)
            {
                int submitDays = await GetContestPolicyDaysAsync(
                    contestId, ContestPolicyKeys.AppealSubmitDays, DEFAULT_APPEAL_SUBMIT_DAYS, configRepo);
                int reviewDays = await GetContestPolicyDaysAsync(
                    contestId, ContestPolicyKeys.AppealReviewDays, DEFAULT_APPEAL_REVIEW_DAYS, configRepo);
                int judgeDays = await GetContestPolicyDaysAsync(
                    contestId, ContestPolicyKeys.JudgeRescoreDays, DEFAULT_JUDGE_RESCORE_DAYS, configRepo);

                bool prevIsManual = IsManualRound(previousRound);

                int bufferDays;
                if (previousRound.IsRetakeRound)
                {
                    bufferDays = prevIsManual ? judgeDays : 0;
                }
                else
                {
                    bufferDays = prevIsManual
                        ? (judgeDays * 2 + submitDays + reviewDays)
                        : (submitDays + reviewDays);
                }

                DateTime minStart = previousRound.End.AddDays(bufferDays);

                if (roundDTO.Start < minStart)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                        $"Round start must be after previous round buffer. Earliest allowed: {DateTimeHelpers.ToIso8601String(minStart)}.");
                }
            }

            // Additional validation for retake round timing vs main round
            Guid? mainRoundId = null;
            bool isRetake = false;
            ProblemTypeEnum? desiredType = null;

            if (roundDTO is CreateRoundDTO createDto && createDto.IsRetakeRound)
            {
                isRetake = true;
                mainRoundId = createDto.MainRoundId;
                desiredType = createDto.ProblemType;
            }
            else if (currentRound != null && currentRound.IsRetakeRound)
            {
                isRetake = true;
                mainRoundId = currentRound.MainRoundId;
                desiredType = roundDTO is UpdateRoundDTO upd ? upd.ProblemType : desiredType;
            }

            if (isRetake)
            {
                if (!mainRoundId.HasValue || mainRoundId == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                        "Retake round must reference a valid main round.");
                }

                Round? mainRound = await roundRepo.Entities
                    .Include(r => r.Problem)
                    .Include(r => r.McqTest)
                    .FirstOrDefaultAsync(r => r.RoundId == mainRoundId && r.DeletedAt == null);

                if (mainRound == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND,
                        "Main round not found for retake.");
                }

                // Ensure no other rounds are scheduled between main round end and retake start
                bool hasInterveningRound = existingRounds
                    .Any(r => r.RoundId != mainRound.RoundId
                              && r.RoundId != (currentRound?.RoundId ?? Guid.Empty)
                              && r.Start < roundDTO.Start
                              && r.Start >= mainRound.End);

                if (hasInterveningRound)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                        "Retake round must be the immediate next round after its main round (no rounds in between).");
                }

                bool mainIsManual = IsManualRound(mainRound);
                int submitDays = await GetContestPolicyDaysAsync(
                    contestId, ContestPolicyKeys.AppealSubmitDays, DEFAULT_APPEAL_SUBMIT_DAYS, configRepo);
                int reviewDays = await GetContestPolicyDaysAsync(
                    contestId, ContestPolicyKeys.AppealReviewDays, DEFAULT_APPEAL_REVIEW_DAYS, configRepo);
                int judgeDays = await GetContestPolicyDaysAsync(
                    contestId, ContestPolicyKeys.JudgeRescoreDays, DEFAULT_JUDGE_RESCORE_DAYS, configRepo);

                int bufferFromMain = mainIsManual
                    ? (judgeDays * 2 + submitDays + reviewDays)
                    : (submitDays + reviewDays);

                DateTime minRetakeStart = mainRound.End.AddDays(bufferFromMain);
                if (roundDTO.Start < minRetakeStart)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                        $"Retake round must start after main round buffer. Earliest allowed: {DateTimeHelpers.ToIso8601String(minRetakeStart)}.");
                }

                // Ensure type alignment is consistent with main
                if (desiredType.HasValue)
                {
                    if (mainIsManual && desiredType != ProblemTypeEnum.Manual)
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                            "Retake of a manual round must also be manual.");
                    }
                    if (!mainIsManual && desiredType == ProblemTypeEnum.Manual)
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                            "Retake of a non-manual round cannot be manual.");
                    }
                }
            }
        }

        public async Task DistributeSubmissionsToJudgesAsync(Guid roundId)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate round exists and get contest information
                Round? round = await _unitOfWork.GetRepository<Round>()
                    .Entities
                    .Include(r => r.Contest)
                    .Include(r => r.Problem)
                    .FirstOrDefaultAsync(r => r.RoundId == roundId && r.DeletedAt == null);

                if (round == null)
                {
                    throw new ErrorException(
                        StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Round not found"
                    );
                }

                // Check if submissions have already been distributed using config service
                bool alreadyDistributed = await _configService.AreSubmissionsDistributedAsync(roundId);

                if (alreadyDistributed)
                {
                    _unitOfWork.CommitTransaction();
                    return;
                }

                // Check if round has a manual problem
                if (round.Problem == null || round.Problem.Type != ProblemTypeEnum.Manual.ToString())
                {
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "This round does not have a manual problem type"
                    );
                }

                // Get all judges for this contest
                IList<JudgeInContestDTO> judges = await _contestJudgeService.GetJudgesByContestAsync(round.ContestId);

                if (judges == null || !judges.Any())
                {
                    throw new ErrorException(
                        StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "No judges available for this contest"
                    );
                }

                // Filter only active judges
                List<JudgeInContestDTO> activeJudges = judges.Where(j => j.Status.ToLower() == JUDGE_STATUS_ACTIVE).ToList();

                if (!activeJudges.Any())
                {
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "No active judges available for submission distribution"
                    );
                }

                // Get pending submissions for this round
                List<Submission> pendingSubmissions = await _unitOfWork.GetRepository<Submission>()
                    .Entities
                    .Include(s => s.Team)
                    .Where(s => s.Problem.RoundId == roundId
                                && !s.DeletedAt.HasValue
                                && s.Status == SubmissionStatusEnum.Pending.ToString()
                                && (string.IsNullOrWhiteSpace(s.JudgedBy)))
                    .OrderBy(s => s.CreatedAt)
                    .ToListAsync();

                if (!pendingSubmissions.Any())
                {
                    // No pending submissions to distribute
                    _unitOfWork.CommitTransaction();
                    return;
                }

                // Distribute submissions equally using round-robin algorithm
                int judgeIndex = 0;
                IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                int judgeDays = await GetContestPolicyDaysAsync(
                    round.ContestId, ContestPolicyKeys.JudgeRescoreDays, DEFAULT_JUDGE_RESCORE_DAYS, configRepo);
                DateTime judgeDeadline = round.End.AddDays(judgeDays);

                // Assign submissions to judges
                foreach (Submission submission in pendingSubmissions)
                {
                    JudgeInContestDTO assignedJudge = activeJudges[judgeIndex];

                    submission.JudgedBy = assignedJudge.UserId.ToString();

                    submissionRepo.Update(submission);

                    // Set judge deadline for this submission
                    string key = ConfigKeys.JudgeSubmissionDeadline(assignedJudge.UserId, submission.SubmissionId);
                    Config? existing = await configRepo.Entities.FirstOrDefaultAsync(c => c.Key == key);
                    if (existing == null)
                    {
                        await configRepo.InsertAsync(new Config
                        {
                            Key = key,
                            Value = judgeDeadline.ToString("o"),
                            Scope = SCOPE_CONTEST,
                            UpdatedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        existing.Value = judgeDeadline.ToString("o");
                        existing.Scope = SCOPE_CONTEST;
                        existing.UpdatedAt = DateTime.UtcNow;
                        await configRepo.UpdateAsync(existing);
                    }

                    // Move to next judge
                    judgeIndex = (judgeIndex + 1) % activeJudges.Count;
                }

                // Mark submissions as distributed in config service
                await _configService.MarkSubmissionsAsDistributedAsync(roundId);

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // Rollback on error
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(
                    StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error distributing submissions: {ex.Message}"
                );
            }
        }

        private static bool IsManualRound(Round round)
        {
            return round.Problem != null
                && string.Equals(round.Problem.Type, ProblemTypeEnum.Manual.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public async Task<int?> GetRoundTimeLimitSecondsAsync(Guid roundId)
        {
            if (roundId == Guid.Empty)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Round ID cannot be empty.");

            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            Round? round = await roundRepo.Entities
                .FirstOrDefaultAsync(r => r.RoundId == roundId && !r.DeletedAt.HasValue);

            if (round == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Round not found.");

            string key = ConfigKeys.RoundTimeLimitSeconds(roundId);

            string? value = await configRepo.Entities
                .Where(c => c.Key == key && c.Scope == SCOPE_CONTEST && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(value))
                return null;

            return int.TryParse(value, out int secs) ? secs : (int?)null;
        }

        private static async Task UpsertConfigAsync(IGenericRepository<Config> repo, string key, string value)
        {
            Config? existing = await repo.Entities.FirstOrDefaultAsync(c => c.Key == key);

            if (existing == null)
            {
                await repo.InsertAsync(new Config
                {
                    Key = key,
                    Value = value,
                    Scope = SCOPE_CONTEST,
                    UpdatedAt = DateTime.UtcNow,
                    DeletedAt = null
                });
            }
            else
            {
                existing.Value = value;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.DeletedAt = null;
                await repo.UpdateAsync(existing);
            }
        }

        public async Task MarkFinishFinishRoundAsync(Guid roundId)
        {
            // Get user ID from JWT token
            string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    $"Null User Id");

            // Get student ID from user ID
            IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
            Guid studentId = studentRepo.Entities.Where(s => s.UserId.ToString() == userId)
                .Select(s => s.StudentId)
                .FirstOrDefault();

            // Check if student has already finished this round
            bool IsAlreadyFinishedRound = await _configService.IsStudentFinishedRoundAsync(roundId, studentId);

            if (IsAlreadyFinishedRound)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    $"Cannot end round. You have already finished this round.");
            }

            // Mark student as finished for this round
            await _configService.MarkFinishedSubmissionAsync(roundId, studentId);
        }

        public async Task<string> GenerateOpenCode(Guid roundId)
        {
            try
            {
                // Begin transaction
                _unitOfWork.BeginTransaction();

                // Validate round exists
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                Round? round = await roundRepo.Entities
                    .FirstOrDefaultAsync(r => r.RoundId == roundId && !r.DeletedAt.HasValue);

                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Round not found.");
                }

                // Retrieve open code from config
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
                string key = ConfigKeys.RoundOpenCode(roundId);

                Config? config = await configRepo.Entities
                    .FirstOrDefaultAsync(c => c.Key == key && c.DeletedAt == null);

                // Generate 4-digit random code
                Random random = new Random();
                string openCode = random.Next(OPEN_CODE_MIN, OPEN_CODE_MAX).ToString();

                // Create open code in config if not exists, else update it
                if (config == null)
                {
                    await UpsertConfigAsync(configRepo, key, openCode);
                }
                else
                {
                    config.Value = openCode;
                    configRepo.Update(config);
                }

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit transaction
                _unitOfWork.CommitTransaction();

                return openCode;
            }
            catch (Exception ex)
            {
                // Rollback on error
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error generating open code: {ex.Message}");
            }
        }

        public async Task ValidateOpenCode(Guid roundId, string? openCode)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(openCode))
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Open code cannot be empty.");
                }

                // Check if open code exists in config
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
                string key = ConfigKeys.RoundOpenCode(roundId);

                Config? config = await configRepo.Entities
                    .FirstOrDefaultAsync(c => c.Key == key && c.DeletedAt == null);

                if (config == null || config.Value != openCode)
                {
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        ResponseCodeConstants.FORBIDDEN,
                        "Invalid open code.");
                }
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error validating open code: {ex.Message}");
            }
        }
        public async Task<string> GetOpenCode(Guid roundId)
        {
            // Validate round exists
            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
            Round? round = await roundRepo.Entities
                .FirstOrDefaultAsync(r => r.RoundId == roundId && !r.DeletedAt.HasValue);

            if (round == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    "Round not found.");
            }

            // Retrieve open code from config
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
            string key = ConfigKeys.RoundOpenCode(roundId);

            Config? config = await configRepo.Entities
                .FirstOrDefaultAsync(c => c.Key == key && c.DeletedAt == null);

            // Validate open code exists
            if (config == null || config.Value == null)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    "Open code not found.");
            }

            return config.Value;
        }

        public async Task<GetRoundDTO> StartRoundNowAsync(Guid roundId)
        {
            bool committed = false;

            // fields to track state
            Guid contestId = Guid.Empty;
            string roundName = string.Empty;
            Guid persistedRoundId = roundId;

            try
            {
                _unitOfWork.BeginTransaction();

                if (roundId == Guid.Empty)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid round ID.");

                Round round = await GetRoundOwnedByCurrentOrganizerAsync(roundId);
                await EnsurePreviousRoundFinalizedAsync(round);

                DateTime now = DateTime.UtcNow;

                if (now >= round.End)
                    throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Round already ended. Cannot start now.");

                // Round cannot be started before contest start
                if (round.Contest?.Start.HasValue == true && now < round.Contest.Start.Value)
                    throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Cannot start round before contest start.");

                if (round.Contest?.End.HasValue == true && now >= round.Contest.End.Value)
                    throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Cannot start round because contest already ended.");

                // Prevent overlap with other rounds
                var roundRepo = _unitOfWork.GetRepository<Round>();
                var otherRounds = await roundRepo.Entities
                    .Where(r => r.ContestId == round.ContestId && r.RoundId != roundId && r.DeletedAt == null)
                    .ToListAsync();

                foreach (var other in otherRounds)
                {
                    if (now <= other.End && round.End >= other.Start)
                        throw new ErrorException(StatusCodes.Status409Conflict, "DATE_CONFLICT",
                        $"New round time conflicts with existing round '{other.Name}' ({DateTimeHelpers.ToIso8601String(other.Start)} - {DateTimeHelpers.ToIso8601String(other.End)}).");
                }

                round.Start = now;
                round.Status = RoundStatusEnum.Opened.ToString();

                await roundRepo.UpdateAsync(round);
                await _unitOfWork.SaveAsync();

                _unitOfWork.CommitTransaction();

                committed = true;
                contestId = round.ContestId;
                roundName = round.Name;
                persistedRoundId = round.RoundId;

            }
            catch
            {
                if (!committed) _unitOfWork.RollBack();
                throw;
            }

            // Generate initial open code immediately
            try
            {
                await GenerateOpenCode(persistedRoundId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate initial open code for round {RoundId}", persistedRoundId);
            }

            // Schedule recurring open code regeneration every 1 minute
            SafeEnqueue(() =>
                RecurringJob.AddOrUpdate(
                    $"regenerate-open-code-{persistedRoundId}",
                    () => RegenerateOpenCodeAsync(persistedRoundId),
                    Cron.Minutely),
                "ScheduleOpenCodeRegeneration");

            // Activity log
            var actorId = GetCurrentUserGuidOrThrow();

            await SafeWriteActivityAsync(actorId,
                ActivityActions.RoundStartNow,
                TargetTypes.Round,
                persistedRoundId.ToString());

            // Notification to participants
            await SafeNotifyContestParticipantsAsync(contestId,
                NotificationTypes.RoundStarted,
                new
                {
                    contestId = contestId,
                    roundId = persistedRoundId,
                    name = roundName,
                    targetType = TargetTypes.Round,
                    targetId = persistedRoundId.ToString(),
                    message = $"Round '{roundName}' has started."
                });

            // Schedule background job to handle state transitions
            SafeEnqueue(() =>
                BackgroundJob.Enqueue<RoundStateJob>(job => job.ScheduleRoundStateTransitionsAsync(persistedRoundId)),
                "ScheduleRoundStateTransitionsAsync");

            return await GetRoundByIdAsync(persistedRoundId, null);
        }

        public async Task<GetRoundDTO> EndRoundNowAsync(Guid roundId)
        {

            // fields to track state
            bool committed = false;
            Guid contestId = Guid.Empty;
            string roundName = string.Empty;
            Guid persistedRoundId = roundId;
            bool isManual = false;

            try
            {
                _unitOfWork.BeginTransaction();

                if (roundId == Guid.Empty)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid round ID.");

                Round round = await GetRoundOwnedByCurrentOrganizerAsync(roundId);

                DateTime now = DateTime.UtcNow;

                if (now <= round.Start)
                    throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Cannot end round before it starts.");

                round.End = now;
                round.Status = RoundStatusEnum.Closed.ToString();

                var roundRepo = _unitOfWork.GetRepository<Round>();
                await roundRepo.UpdateAsync(round);
                await _unitOfWork.SaveAsync();

                _unitOfWork.CommitTransaction();
                committed = true;

                // Define the recurring job ID
                string recurringJobId = $"regenerate-open-code-{persistedRoundId}";

                // Cancel any pending open code regeneration jobs
                RecurringJob.RemoveIfExists(recurringJobId);

                // capture for later use
                contestId = round.ContestId;
                roundName = round.Name;
                persistedRoundId = round.RoundId;
                isManual = (round.Problem != null && round.Problem.Type == ProblemTypeEnum.Manual.ToString());
            }
            catch
            {
                if (!committed) _unitOfWork.RollBack();
                throw;
            }

            // If manual round, distribute pending submissions immediately
            if (isManual)
                try
                {
                    await DistributeSubmissionsToJudgesAsync(persistedRoundId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DistributeSubmissionsToJudgesAsync failed after ending round {RoundId}", persistedRoundId);
                }

            // activity log
            var actorId = GetCurrentUserGuidOrThrow();

            await SafeWriteActivityAsync(actorId,
                ActivityActions.RoundEndNow,
                TargetTypes.Round,
                persistedRoundId.ToString());

            // notification to participants
            await SafeNotifyContestParticipantsAsync(contestId,
                NotificationTypes.RoundEnded,
                new
                {
                    contestId = contestId,
                    roundId = persistedRoundId,
                    name = roundName,
                    targetType = TargetTypes.Round,
                    targetId = persistedRoundId.ToString(),
                    message = $"Round '{roundName}' has ended."
                });

            // Schedule background job to handle state transitions
            SafeEnqueue(() =>
                BackgroundJob.Enqueue<RoundStateJob>(job => job.ScheduleRoundStateTransitionsAsync(persistedRoundId)),
                "ScheduleRoundStateTransitionsAsync");

            return await GetRoundByIdAsync(persistedRoundId, null);
        }


        public async Task<string?> GetOrganizerMockTestTemplateUrl()
        {
            try
            {
                // Get config repository
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Get config key
                string key = ConfigKeys.OrganizerMockTestTemplate();

                // Retrieve attachment ID from config
                Config? config = await configRepo.Entities
                    .Where(c => c.Key == key && c.DeletedAt == null)
                    .FirstOrDefaultAsync();

                if (config == null || string.IsNullOrWhiteSpace(config.Value))
                {
                    return null;
                }

                // Parse attachment ID
                if (!Guid.TryParse(config.Value, out Guid attachmentId))
                {
                    throw new ErrorException(StatusCodes.Status500InternalServerError,
                        ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                        "Invalid attachment ID in configuration.");
                }

                // Get attachment repository
                IGenericRepository<Attachment> attachmentRepo = _unitOfWork.GetRepository<Attachment>();

                // Retrieve attachment
                Attachment? attachment = await attachmentRepo.Entities
                    .Where(a => a.AttachmentId == attachmentId && !a.DeletedAt.HasValue)
                    .FirstOrDefaultAsync();

                if (attachment == null)
                {
                    return null;
                }

                return attachment.Url;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving organizer mock test template: {ex.Message}");
            }
        }

        public async Task<string?> GetStudentMockTestTemplateUrl()
        {
            try
            {
                // Get config repository
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Get config key
                string key = ConfigKeys.StudentMockTestTemplateForOrganizer();

                // Retrieve attachment ID from config
                Config? config = await configRepo.Entities
                    .Where(c => c.Key == key && c.DeletedAt == null)
                    .FirstOrDefaultAsync();

                if (config == null || string.IsNullOrWhiteSpace(config.Value))
                {
                    return null;
                }

                // Parse attachment ID
                if (!Guid.TryParse(config.Value, out Guid attachmentId))
                {
                    throw new ErrorException(StatusCodes.Status500InternalServerError,
                        ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                        "Invalid attachment ID in configuration.");
                }

                // Get attachment repository
                IGenericRepository<Attachment> attachmentRepo = _unitOfWork.GetRepository<Attachment>();

                // Retrieve attachment
                Attachment? attachment = await attachmentRepo.Entities
                    .Where(a => a.AttachmentId == attachmentId && !a.DeletedAt.HasValue)
                    .FirstOrDefaultAsync();

                if (attachment == null)
                {
                    return null;
                }

                return attachment.Url;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving student mock test template: {ex.Message}");
            }
        }


        public async Task RegenerateOpenCodeAsync(Guid roundId)
        {
            try
            {
                // Check if round is still in Opened status
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                Round? round = await roundRepo.Entities
                    .FirstOrDefaultAsync(r => r.RoundId == roundId && !r.DeletedAt.HasValue);

                // Define the recurring job ID
                string recurringJobId = $"regenerate-open-code-{roundId}";

                // If round not found, is closed, or has ended, remove the recurring job
                if (round == null || round.Status != RoundStatusEnum.Opened.ToString() || DateTime.UtcNow >= round.End)
                {
                    RecurringJob.RemoveIfExists(recurringJobId);
                    _logger.LogInformation("Removed open code regeneration job for round {RoundId} (round ended or closed)", roundId);
                    return;
                }

                // Generate new open code
                string newOpenCode = await GenerateOpenCode(roundId);

                _logger.LogInformation("Successfully regenerated open code for round {RoundId}: {OpenCode}", roundId, newOpenCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to regenerate open code for round {RoundId}", roundId);
            }
        }

        private async Task EnsurePreviousRoundFinalizedAsync(Round round)
        {
            var roundRepo = _unitOfWork.GetRepository<Round>();
            var submissionRepo = _unitOfWork.GetRepository<Submission>();
            var appealRepo = _unitOfWork.GetRepository<Appeal>();

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
                    && (a.State != AppealStateEnum.Closed.ToString()
                        || a.Decision == AppealDecisionEnum.Pending.ToString()));

            if (hasUnfinishedSubmissions || hasPendingAppeals)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    $"Cannot start round '{round.Name}' because previous round '{prevRound.Name}' is not finalized.");
            }
        }
        private string GetCurrentUserIdOrThrow()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || !user.Identity?.IsAuthenticated == true)
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Sign in required.");

            var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(id))
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Invalid user context.");

            return id;
        }

        private async Task<Round> GetRoundOwnedByCurrentOrganizerAsync(Guid roundId)
        {
            string currentUserId = GetCurrentUserIdOrThrow();

            var roundRepo = _unitOfWork.GetRepository<Round>();
            Round? round = await roundRepo.Entities
                .Where(r => r.RoundId == roundId && r.DeletedAt == null)
                .Include(r => r.Contest)
                .Include(r => r.Problem)
                .FirstOrDefaultAsync();

            if (round == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Round not found.");

            if (round.Contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

            if (!string.Equals(round.Contest.CreatedBy, currentUserId, StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(StatusCodes.Status403Forbidden, "FORBIDDEN", "Only the organizer who created this contest can modify rounds.");

            return round;
        }

        // Get current user GUID from JWT token or throw error
        private Guid GetCurrentUserGuidOrThrow()
        {
            string idStr = GetCurrentUserIdOrThrow();
            if (!Guid.TryParse(idStr, out var id) || id == Guid.Empty)
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Invalid user id.");
            return id;
        }

        // Get all participant user IDs (students and mentors) for a given contest
        private async Task<List<Guid>> GetParticipantUserIdsByContestAsync(Guid contestId)
        {
            var teamRepo = _unitOfWork.GetRepository<Team>();

            var studentIds = await teamRepo.Entities
                .AsNoTracking()
                .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                .SelectMany(t => t.TeamMembers.Select(tm => tm.Student.UserId))
                .ToListAsync();

            var mentorIds = await teamRepo.Entities
                .AsNoTracking()
                .Where(t => t.ContestId == contestId && t.DeletedAt == null && t.MentorId != Guid.Empty)
                .Select(t => t.Mentor.UserId)
                .ToListAsync();

            return studentIds.Concat(mentorIds)
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();
        }

        // Write activity log safely, logging any exceptions
        private async Task SafeWriteActivityAsync(Guid actorId, string action, string targetType, string targetId)
        {
            try { await _activityLogWriter.TryWriteAsync(actorId, action, targetType, targetId); }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Activity log failed. Action={Action}, Target={TargetType}, TargetId={TargetId}, ActorId={ActorId}",
                    action, targetType, targetId, actorId);
            }
        }

        // Notify contest participants safely, logging any exceptions
        private async Task SafeNotifyContestParticipantsAsync(Guid contestId, string type, object payload)
        {
            try
            {
                var ids = await GetParticipantUserIdsByContestAsync(contestId);
                if (ids.Count == 0) return;
                await _notificationService.CreateInAppToUsersAsync(ids, type, payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notify participants failed. ContestId={ContestId}, Type={Type}", contestId, type);
            }
        }

        // Enqueue a Hangfire job safely, logging any exceptions
        private void SafeEnqueue(Action enqueue, string jobName)
        {
            try { enqueue(); }
            catch (Exception ex) { _logger.LogError(ex, "Hangfire enqueue failed: {JobName}", jobName); }
        }

        private async Task<int> GetRoundRankCutoffAsync(Guid roundId)
        {
            var configRepo = _unitOfWork.GetRepository<Config>();
            string key = ConfigKeys.RoundRankCutoff(roundId);

            string? value = await configRepo.Entities
                .AsNoTracking()
                .Where(c => c.Key == key && c.Scope == "contest" && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(value)) return 0;
            return (int.TryParse(value, out int n) && n > 0) ? n : 0;
        }

        private async Task<Round?> FindPreviousMainRoundAsync(Round currentRound)
        {
            var roundRepo = _unitOfWork.GetRepository<Round>();

            return await roundRepo.Entities
                .AsNoTracking()
                .Where(r => r.ContestId == currentRound.ContestId
                            && r.RoundId != currentRound.RoundId
                            && r.DeletedAt == null
                            && !r.IsRetakeRound
                            && r.End <= currentRound.Start)
                .OrderByDescending(r => r.End)
                .Include(r => r.Problem)
                .Include(r => r.McqTest)
                .FirstOrDefaultAsync();
        }

        private async Task<Round?> FindRetakeRoundAsync(Guid mainRoundId)
        {
            var roundRepo = _unitOfWork.GetRepository<Round>();

            return await roundRepo.Entities
                .AsNoTracking()
                .Where(r => r.MainRoundId == mainRoundId
                            && r.IsRetakeRound
                            && r.DeletedAt == null)
                .OrderBy(r => r.Start)
                .Include(r => r.Problem)
                .Include(r => r.McqTest)
                .FirstOrDefaultAsync();
        }

        private async Task<bool> IsRoundFinalizedAsync(Guid roundId)
        {
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
            IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();

            bool hasPendingSubs = await submissionRepo.Entities
                .AsNoTracking()
                .AnyAsync(s => s.DeletedAt == null
                               && s.Problem != null
                               && s.Problem.RoundId == roundId
                               && s.Status == SUBMISSION_STATUS_PENDING);

            if (hasPendingSubs) return false;

            bool hasOpenAppeals = await appealRepo.Entities
                .AsNoTracking()
                .AnyAsync(a => a.DeletedAt == null
                               && a.TargetId == roundId
                               && (a.State != APPEAL_STATE_CLOSED
                                   || a.Decision == AppealDecisionEnum.Pending.ToString()));

            return !hasOpenAppeals;
        }

        private async Task EnforceRoundRankCutoffForStudentAsync(Round currentRound, Guid studentId)
        {
            if (currentRound.IsRetakeRound) return; // retake handled by appeal logic

            int cutoff = await GetRoundRankCutoffAsync(currentRound.RoundId);
            if (cutoff <= 0) return; // disabled

            Round? prevRound = await FindPreviousMainRoundAsync(currentRound);
            if (prevRound == null) return; // first main round, next

            // If retake exists for prevRound, enforce only after retake finalized; otherwise after main finalized
            Round? retakeRound = await FindRetakeRoundAsync(prevRound.RoundId);
            if (retakeRound != null)
            {
                bool retakeFinalized = await IsRoundFinalizedAsync(retakeRound.RoundId);
                if (!retakeFinalized) return; // wait until retake done
                prevRound = retakeRound;
            }
            else
            {
                bool prevFinalized = await IsRoundFinalizedAsync(prevRound.RoundId);
                if (!prevFinalized) return; // wait until main round done
            }

            // Find student's team in this contest
            var teamRepo = _unitOfWork.GetRepository<Team>();
            Guid teamId = await teamRepo.Entities
                .AsNoTracking()
                .Where(t => t.ContestId == currentRound.ContestId
                            && t.DeletedAt == null
                            && t.TeamMembers.Any(tm => tm.StudentId == studentId))
                .Select(t => t.TeamId)
                .FirstOrDefaultAsync();

            if (teamId == Guid.Empty)
                throw new ErrorException(StatusCodes.Status403Forbidden, ResponseCodeConstants.FORBIDDEN,
                    "You are not in a team for this contest.");

            // Top-N teams from previous round (score desc, tie -> earliest submission.CreatedAt)
            List<Guid> topTeamIds = await GetTopTeamsByRoundAsync(currentRound.ContestId, prevRound.RoundId, cutoff);

            if (!topTeamIds.Contains(teamId))
                throw new ErrorException(StatusCodes.Status403Forbidden, ResponseCodeConstants.FORBIDDEN,
                    $"Your team is not in Top-{cutoff} of the previous round.");
        }

        private sealed class TeamRankRow
        {
            public Guid TeamId { get; set; }
            public double AvgScore { get; set; }
            public double AvgCreatedAtTicks { get; set; }
        }

        private async Task<List<Guid>> GetTopTeamsByRoundAsync(Guid contestId, Guid prevRoundId, int cutoff)
        {
            if (cutoff <= 0) return new List<Guid>();

            // 1) Load team members in contest
            var teamRepo = _unitOfWork.GetRepository<Team>();

            var memberPairs = await teamRepo.Entities
                .AsNoTracking()
                .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                .SelectMany(t => t.TeamMembers
                    .Select(tm => new { t.TeamId, tm.StudentId }))
                .ToListAsync();

            var teamMembers = memberPairs
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.StudentId).Distinct().ToList());

            if (teamMembers.Count == 0) return new List<Guid>();

            // 2) Load all submissions in prev round 
            var submissionRepo = _unitOfWork.GetRepository<Submission>();

            var subs = await submissionRepo.Entities
                .AsNoTracking()
                .Where(s => s.DeletedAt == null
                            && s.Problem != null
                            && s.Problem.RoundId == prevRoundId)
                .Select(s => new
                {
                    s.SubmissionId,
                    s.TeamId,
                    s.SubmittedByStudentId,
                    s.Score,
                    s.Status,
                    s.CreatedAt
                })
                .ToListAsync();

            // Latest submission per (TeamId, StudentId)
            var latestByTeamStudent = subs
                .GroupBy(x => (x.TeamId, x.SubmittedByStudentId))
                .Select(g => g.OrderByDescending(x => x.CreatedAt)
                              .ThenByDescending(x => x.SubmissionId)
                              .First())
                .ToDictionary(x => (x.TeamId, x.SubmittedByStudentId), x => x);

            // 3) Compute avg score + avg createdAt
            var rows = new List<TeamRankRow>(teamMembers.Count);

            foreach (var kv in teamMembers)
            {
                Guid teamId = kv.Key;
                List<Guid> members = kv.Value;
                if (members.Count == 0) continue;

                double sumScore = 0.0;
                double sumTicks = 0.0;

                foreach (var studentId in members)
                {
                    if (latestByTeamStudent.TryGetValue((teamId, studentId), out var last))
                    {
                        bool finished = string.Equals(last.Status, SubmissionStatusEnum.Finished.ToString(), StringComparison.OrdinalIgnoreCase);

                        // Score only when latest is Finished
                        if (finished) sumScore += last.Score;

                        // Tie-break: only use CreatedAt when latest is Finished, else MaxValue
                        sumTicks += finished ? last.CreatedAt.Ticks : DateTime.MaxValue.Ticks;
                    }
                    else
                    {
                        // no submission -> score 0, createdAt loses tie-break
                        sumTicks += DateTime.MaxValue.Ticks;
                    }
                }

                rows.Add(new TeamRankRow
                {
                    TeamId = teamId,
                    AvgScore = sumScore / members.Count,
                    AvgCreatedAtTicks = sumTicks / members.Count
                });
            }

            return rows
                .OrderByDescending(r => r.AvgScore)
                .ThenBy(r => r.AvgCreatedAtTicks) 
                .ThenBy(r => r.TeamId)            
                .Take(cutoff)
                .Select(r => r.TeamId)
                .ToList();
        }

        /// <summary>
        /// Validates create round input parameters
        /// </summary>
        private void ValidateCreateRoundInput(CreateRoundDTO roundDTO)
        {
            if (roundDTO == null)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST, "Round data cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(roundDTO.Name))
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST, "Round name is required.");
            }

            // Validate problem type specific configurations
            ValidateProblemTypeConfiguration(roundDTO);
        }

        /// <summary>
        /// Validates problem type specific configuration
        /// </summary>
        private void ValidateProblemTypeConfiguration(CreateRoundDTO roundDTO)
        {
            switch (roundDTO.ProblemType)
            {
                case ProblemTypeEnum.Manual:
                case ProblemTypeEnum.AutoEvaluation:
                    if (roundDTO.ProblemConfig == null)
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            "Problem configuration is required for the selected problem type.");
                    }

                    // Validate penalty range
                    if (roundDTO.ProblemConfig.PenaltyRate.HasValue &&
                        (roundDTO.ProblemConfig.PenaltyRate.Value < 0 || roundDTO.ProblemConfig.PenaltyRate.Value > 1))
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            "Penalty rate must be between 0 and 1");
                    }
                    break;

                case ProblemTypeEnum.McqTest:

                    break;

                default:
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Invalid problem type.");
            }
        }

        /// <summary>
        /// Creates round entity from DTO
        /// </summary>
        private Round CreateRoundEntity(Guid contestId, CreateRoundDTO roundDTO)
        {
            Round round = _mapper.Map<Round>(roundDTO);

            // Store times in database
            round.Start = roundDTO.Start;
            round.End = roundDTO.End;
            round.ContestId = contestId;

            // Set retake round properties
            round.IsRetakeRound = roundDTO.IsRetakeRound;
            round.MainRoundId = roundDTO.MainRoundId;

            // Set initial status based on current time
            DateTime now = DateTime.UtcNow;
            if (now < round.Start)
                round.Status = ROUND_STATUS_INCOMING;
            else if (now >= round.End)
                round.Status = ROUND_STATUS_CLOSED;
            else
                round.Status = ROUND_STATUS_OPENED;

            return round;
        }

        /// <summary>
        /// Configures round settings (time limit and rank cutoff)
        /// </summary>
        private async Task ConfigureRoundSettingsAsync(
            Guid roundId,
            Guid contestId,
            DateTime roundEndUtc,
            CreateRoundDTO roundDTO,
            IGenericRepository<Config> configRepo)
        {
            // Store time limit in config
            if (roundDTO.TimeLimitSeconds.HasValue && roundDTO.TimeLimitSeconds.Value > 0)
            {
                await UpsertConfigAsync(
                    configRepo,
                    ConfigKeys.RoundTimeLimitSeconds(roundId),
                    roundDTO.TimeLimitSeconds.Value.ToString()
                );
            }

            // Store rank cutoff in config
            if (roundDTO.RankCutoff.HasValue)
            {
                if (roundDTO.RankCutoff.Value < 0)
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "RankCutoff must be >= 0 (0 = disabled).");

                await UpsertConfigAsync(
                    configRepo,
                    ConfigKeys.RoundRankCutoff(roundId),
                    roundDTO.RankCutoff.Value.ToString()
                );
            }

            await UpsertRoundDeadlineConfigsAsync(roundId, contestId, roundEndUtc, configRepo);
        }

        /// <summary>
        /// Creates round content (MCQ test or Problem) based on type
        /// </summary>
        private async Task CreateRoundContentAsync(Guid roundId, CreateRoundDTO roundDTO)
        {
            switch (roundDTO.ProblemType)
            {
                case ProblemTypeEnum.McqTest:
                    await CreateMcqTestForRoundAsync(roundId, roundDTO.McqTestConfig!);
                    break;

                case ProblemTypeEnum.AutoEvaluation:
                    await CreateAutoEvaluationProblemAsync(roundId, roundDTO.ProblemConfig!);
                    break;

                case ProblemTypeEnum.Manual:
                    await CreateManualProblemAsync(roundId, roundDTO.ProblemConfig!);
                    break;
            }
        }

        /// <summary>
        /// Creates MCQ test for round
        /// </summary>
        private async Task CreateMcqTestForRoundAsync(Guid roundId, CreateMcqTestDTO config)
        {
            await _mcqTestService.CreateMcqTestAsync(roundId, config);
        }

        /// <summary>
        /// Creates auto-evaluation problem for round
        /// </summary>
        private async Task CreateAutoEvaluationProblemAsync(Guid roundId, CreateProblemDTO config)
        {
            // Validate test type
            if (config.TestType == null)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Test type is required for auto-evaluation problems.");
            }

            await _problemService.CreateProblemAsync(roundId, config);

            // Upload template file
            if (config.TemplateFile != null)
            {
                await UploadProblemTemplateAsync(roundId, config.TemplateFile);
            }

            // Set mock test weight
            if (config.MockTestWeight != null) 
            {
                await AssignWeightToRoundAsync(roundId, config.MockTestWeight.Value);
            }
        }

        /// <summary>
        /// Creates manual problem for round
        /// </summary>
        private async Task CreateManualProblemAsync(Guid roundId, CreateProblemDTO config)
        {
            await _problemService.CreateProblemAsync(roundId, config);

            // Upload template file
            if (config.TemplateFile != null)
            {
                await UploadProblemTemplateAsync(roundId, config.TemplateFile);
            }
        }

        /// <summary>
        /// Assign weight to round
        /// </summary>
        private async Task AssignWeightToRoundAsync(Guid roundId, double weight)
        {
            string roundWeightKey = ConfigKeys.RoundWeight(roundId);

            await _configService.SetConfigValueAsync(roundWeightKey, weight.ToString(), SCOPE_ROUND);
        }

        /// <summary>
        /// Uploads problem template file and updates problem entity
        /// </summary>
        private async Task UploadProblemTemplateAsync(Guid roundId, IFormFile templateFile)
        {
            IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();

            // Upload new template
            string uploadedUrl = await _cloudinaryService.UploadFileAsync(templateFile, CODE_TEMPLATE_FOLDER);

            // Load the created problem and set TemplateUrl
            Problem? createdProblem = await problemRepo.Entities
                .Where(p => p.RoundId == roundId && p.DeletedAt == null)
                .FirstOrDefaultAsync();

            if (createdProblem != null)
            {
                createdProblem.TemplateUrl = uploadedUrl;
                await problemRepo.UpdateAsync(createdProblem);
            }
        }

        /// <summary>
        /// Performs post-creation operations (logging and scheduling)
        /// </summary>
        private async Task PerformPostCreateRoundOperationsAsync(Round createdRound)
        {
            // Activity log
            var actorId = GetCurrentUserGuidOrThrow();
            await SafeWriteActivityAsync(actorId,
                ActivityActions.RoundCreate,
                TargetTypes.Round,
                createdRound.RoundId.ToString());

            // Schedule state transitions
            SafeEnqueue(() =>
                BackgroundJob.Enqueue<RoundStateJob>(job => job.ScheduleRoundStateTransitionsAsync(createdRound.RoundId)),
                "ScheduleRoundStateTransitionsAsync");
        }

        /// <summary>
        /// Validates update round input parameters
        /// </summary>
        private void ValidateUpdateRoundInput(Guid id, UpdateRoundDTO roundDTO)
        {
            if (id == Guid.Empty)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST, "Round ID cannot be empty.");
            }

            if (roundDTO == null)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST, "Round data cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(roundDTO.Name))
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST, "Round name is required.");
            }

            if (roundDTO.Start > roundDTO.End)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST, "Start date cannot be later than end date.");
            }
        }

        /// <summary>
        /// Gets existing round or throws if not found
        /// </summary>
        private async Task<Round> GetExistingRoundOrThrowAsync(IGenericRepository<Round> roundRepo, Guid id)
        {
            Round? round = await roundRepo.Entities
                .Where(r => r.RoundId == id)
                .Include(r => r.McqTest)
                .Include(r => r.Problem)
                .FirstOrDefaultAsync();

            if (round == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND, "Round not found.");
            }

            return round;
        }

        /// <summary>
        /// Updates round entity with DTO values
        /// </summary>
        private async Task UpdateRoundEntityAsync(Round round, UpdateRoundDTO roundDTO, IGenericRepository<Config> configRepo)
        {
            // Update basic properties
            _mapper.Map(roundDTO, round);

            // Store times in database
            round.Start = roundDTO.Start;
            round.End = roundDTO.End;

            // Update problem type specific content
            await UpdateRoundContentAsync(round, roundDTO);

            // Update configurations
            await UpdateRoundConfigurationsAsync(round.RoundId, round.ContestId, round.End, roundDTO, configRepo);
        }

        /// <summary>
        /// Updates round content based on problem type
        /// </summary>
        private async Task UpdateRoundContentAsync(Round round, UpdateRoundDTO roundDTO)
        {
            switch (roundDTO.ProblemType)
            {
                case ProblemTypeEnum.McqTest:
                    await _mcqTestService.UpdateMcqTestAsync(round.McqTest!.TestId, roundDTO.McqTestConfig!);
                    break;

                case ProblemTypeEnum.AutoEvaluation:
                    await UpdateProblemAsync(round.Problem!, roundDTO.ProblemConfig!);
                    break;
                case ProblemTypeEnum.Manual:
                    await UpdateProblemAsync(round.Problem!, roundDTO.ProblemConfig!);
                    break;

                default:
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST, "Invalid problem type.");
            }
        }

        /// <summary>
        /// Updates problem
        /// </summary>
        private async Task UpdateProblemAsync(Problem problem, UpdateProblemDTO config)
        {
            // Update problem configuration
            await _problemService.UpdateProblemAsync(problem.ProblemId, config);

            // Handle template upload and old file deletion
            if (config.TemplateFile != null)
            {
                string? oldUrl = problem.TemplateUrl;

                // Upload new template
                string uploadedUrl = await _cloudinaryService.UploadFileAsync(config.TemplateFile, CODE_TEMPLATE_FOLDER);

                // Set new url on problem
                problem.TemplateUrl = uploadedUrl;
                await _unitOfWork.GetRepository<Problem>().UpdateAsync(problem);

                // Delete old file if exists and is different
                await DeleteOldTemplateFileAsync(oldUrl, uploadedUrl);
            }

            // Set mock test weight
            if (config.MockTestWeight != null)
            {
                await AssignWeightToRoundAsync(problem.RoundId, config.MockTestWeight.Value);
            }
        }

        /// <summary>
        /// Deletes old template file from Cloudinary
        /// </summary>
        private async Task DeleteOldTemplateFileAsync(string? oldUrl, string newUrl)
        {
            if (string.IsNullOrWhiteSpace(oldUrl) ||
                string.Equals(oldUrl, newUrl, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                string? publicId = CloudinaryHelpers.ExtractCloudinaryPublicId(oldUrl);
                if (!string.IsNullOrWhiteSpace(publicId))
                {
                    await _cloudinaryService.DeleteFileAsync(publicId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old template file: {OldUrl}", oldUrl);
            }
        }

        /// <summary>
        /// Updates round configurations (time limit and rank cutoff)
        /// </summary>
        private async Task UpdateRoundConfigurationsAsync(
            Guid roundId,
            Guid contestId,
            DateTime roundEndUtc,
            UpdateRoundDTO roundDTO,
            IGenericRepository<Config> configRepo)
        {
            // Update time limit config
            if (roundDTO.TimeLimitSeconds.HasValue && roundDTO.TimeLimitSeconds.Value > 0)
            {
                await UpsertConfigAsync(
                    configRepo,
                    ConfigKeys.RoundTimeLimitSeconds(roundId),
                    roundDTO.TimeLimitSeconds.Value.ToString()
                );
            }

            // Update rank cutoff config
            if (roundDTO.RankCutoff.HasValue)
            {
                if (roundDTO.RankCutoff.Value < 0)
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "RankCutoff must be >= 0 (0 = disabled).");

                await UpsertConfigAsync(
                    configRepo,
                    ConfigKeys.RoundRankCutoff(roundId),
                    roundDTO.RankCutoff.Value.ToString()
                );
            }

            await UpsertRoundDeadlineConfigsAsync(roundId, contestId, roundEndUtc, configRepo);
        }

        private async Task UpsertRoundDeadlineConfigsAsync(
            Guid roundId,
            Guid contestId,
            DateTime roundEndUtc,
            IGenericRepository<Config> configRepo)
        {
            int submitDays = await GetContestPolicyDaysAsync(
                contestId, ContestPolicyKeys.AppealSubmitDays, DEFAULT_APPEAL_SUBMIT_DAYS, configRepo);
            int reviewDays = await GetContestPolicyDaysAsync(
                contestId, ContestPolicyKeys.AppealReviewDays, DEFAULT_APPEAL_REVIEW_DAYS, configRepo);

            DateTime submitDeadline = roundEndUtc.AddDays(submitDays);
            DateTime reviewDeadline = submitDeadline.AddDays(reviewDays);

            await UpsertConfigAsync(
                configRepo,
                ConfigKeys.RoundAppealSubmitDeadlineUtc(roundId),
                submitDeadline.ToString("o"));

            await UpsertConfigAsync(
                configRepo,
                ConfigKeys.RoundAppealReviewDeadlineUtc(roundId),
                reviewDeadline.ToString("o"));
        }

        private static async Task<int> GetContestPolicyDaysAsync(
            Guid contestId,
            string policyKey,
            int defaultDays,
            IGenericRepository<Config> configRepo)
        {
            string key = ConfigKeys.ContestPolicy(contestId, policyKey);
            string? value = await configRepo.Entities
                .Where(c => c.Key == key && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            return int.TryParse(value, out int days) && days >= 0 ? days : defaultDays;
        }

        /// <summary>
        /// Performs post-update operations (logging, notifications, scheduling)
        /// </summary>
        private async Task PerformPostUpdateRoundOperationsAsync(Round round)
        {
            // Activity log
            var actorId = GetCurrentUserGuidOrThrow();
            await SafeWriteActivityAsync(actorId,
                ActivityActions.RoundUpdate,
                TargetTypes.Round,
                round.RoundId.ToString());

            // Notification to participants
            await SafeNotifyContestParticipantsAsync(round.ContestId,
                NotificationTypes.RoundUpdated,
                new
                {
                    contestId = round.ContestId,
                    roundId = round.RoundId,
                    name = round.Name,
                    targetType = TargetTypes.Round,
                    targetId = round.RoundId.ToString(),
                    message = $"Round '{round.Name}' has updated."
                });

            // Schedule state transitions
            SafeEnqueue(() =>
                BackgroundJob.Enqueue<RoundStateJob>(job => job.ScheduleRoundStateTransitionsAsync(round.RoundId)),
                "ScheduleRoundStateTransitionsAsync");
        }

        /// <summary>
        /// Validates round ID parameter
        /// </summary>
        private void ValidateRoundId(Guid id)
        {
            if (id == Guid.Empty)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Round ID cannot be empty.");
            }
        }

        /// <summary>
        /// Fetches round with all necessary includes
        /// </summary>
        private async Task<Round> FetchRoundWithIncludesAsync(Guid id)
        {
            IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();

            Round? round = await roundRepo.Entities
                .Where(r => r.RoundId == id && !r.DeletedAt.HasValue)
                .Include(r => r.Contest)
                .Include(r => r.Problem)
                .Include(r => r.McqTest)
                .Include(r => r.MainRound)
                .FirstOrDefaultAsync();

            if (round == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    "Round not found.");
            }

            return round;
        }

        /// <summary>
        /// Loads round configurations
        /// </summary>
        private async Task<(int? timeLimitSeconds, int rankCutoff, double? mockTestRoundWeight)> LoadRoundConfigurationsAsync(Guid roundId)
        {
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            // Load time limit
            string tlKey = ConfigKeys.RoundTimeLimitSeconds(roundId);
            Config? tlConfig = await configRepo.Entities
                .Where(c => c.Key == tlKey && c.Scope == SCOPE_CONTEST && c.DeletedAt == null)
                .FirstOrDefaultAsync();

            int? timeLimitSeconds = null;
            if (tlConfig != null && int.TryParse(tlConfig.Value, out int secs))
            {
                timeLimitSeconds = secs;
            }

            // Load rank cutoff
            string rcKey = ConfigKeys.RoundRankCutoff(roundId);
            Config? rcConfig = await configRepo.Entities
                .Where(c => c.Key == rcKey && c.Scope == SCOPE_CONTEST && c.DeletedAt == null)
                .FirstOrDefaultAsync();

            int rankCutoff = 0;
            if (rcConfig != null && int.TryParse(rcConfig.Value, out int cutoff))
            {
                rankCutoff = cutoff;
            }

            // Load mock test round weight
            string rwKey = ConfigKeys.RoundWeight(roundId);
            Config? rwConfig = await configRepo.Entities
                .Where(c => c.Key == rwKey && c.Scope == SCOPE_ROUND && c.DeletedAt == null)
                .FirstOrDefaultAsync();

            double? mockTestRoundWeight = null;
            if (rwConfig != null && double.TryParse(rwConfig.Value, out double weight))
            {
                mockTestRoundWeight = weight;
            }

            return (timeLimitSeconds, rankCutoff, mockTestRoundWeight);
        }

        /// <summary>
        /// Maps round entity to DTO
        /// </summary>
        private GetRoundDTO MapRoundToDTO(Round round, int? timeLimitSeconds, int rankCutoff, double? mockTestRoundWeight)
        {
            GetRoundDTO roundDTO = _mapper.Map<GetRoundDTO>(round);

            // Map configurations
            roundDTO.TimeLimitSeconds = timeLimitSeconds;
            roundDTO.RankCutoff = rankCutoff;

            // Map problem or MCQ test
            MapRoundContent(roundDTO, round, mockTestRoundWeight);

            return roundDTO;
        }

        /// <summary>
        /// Maps problem or MCQ test content to DTO
        /// </summary>
        private void MapRoundContent(GetRoundDTO roundDTO, Round round, double? mockTestRoundWeight)
        {
            if (round.Problem != null && round.Problem.DeletedAt == null)
            {
                roundDTO.ProblemType = round.Problem.Type;
                roundDTO.Problem = _mapper.Map<GetProblemDTO>(round.Problem);
                roundDTO.Problem.TemplateUrl = round.Problem.TemplateUrl;
                roundDTO.Problem!.MockTestWeight = mockTestRoundWeight;
            }
            else if (round.McqTest != null && round.McqTest.DeletedAt == null)
            {
                roundDTO.ProblemType = ProblemTypeEnum.McqTest.ToString();
                roundDTO.McqTest = _mapper.Map<GetMcqTestDTO>(round.McqTest);
            }
        }

        /// <summary>
        /// Applies student-specific validations
        /// </summary>
        private async Task ApplyStudentValidationsAsync(Round round, string? openCode)
        {
            string? userRole = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrWhiteSpace(userRole) || userRole != RoleConstants.Student)
            {
                return;
            }

            // Get student ID
            Guid studentId = await GetCurrentStudentIdAsync();
            Guid studentUserId = await GetCurrentStudentUserIdAsync(studentId);

            // Validate student is not in an eliminated or disqualified team
            await ValidateStudentQualificationAsync(round.ContestId, studentId);

            // Validate retake round access
            if (round.IsRetakeRound && round.MainRoundId.HasValue)
            {
                await ValidateRetakeRoundAccessAsync(studentUserId, round.MainRoundId.Value);
            }

            // Check if student has finished round
            await ValidateStudentNotFinishedAsync(round.RoundId, studentId);

            // Validate open code
            await ValidateAndMarkOpenCodeAsync(round.RoundId, studentId, openCode);
        }

        /// <summary>
        /// Validates that student is not in an eliminated or disqualified team
        /// </summary>
        private async Task ValidateStudentQualificationAsync(Guid contestId, Guid studentId)
        {
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

            // Find the student's team in this contest
            Team? studentTeam = await teamRepo.Entities
                .Where(t => t.ContestId == contestId
                           && t.DeletedAt == null
                           && t.TeamMembers.Any(tm => tm.StudentId == studentId))
                .FirstOrDefaultAsync();

            // If student has no team, they cannot access the round
            if (studentTeam == null)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    "You are not part of any team in this contest.");
            }

            // Check if the team is eliminated
            if (string.Equals(studentTeam.Status, TeamStatusConstants.Eliminated, StringComparison.OrdinalIgnoreCase))
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    "Your team has been eliminated from this contest and cannot access round information.");
            }

            // Check if the team is disqualified
            if (string.Equals(studentTeam.Status, TeamStatusConstants.Disqualified, StringComparison.OrdinalIgnoreCase))
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    "Your team is disqualified to participated in this contest and cannot access round information.");
            }
        }

        /// <summary>
        /// Gets current student ID from JWT token
        /// </summary>
        private async Task<Guid> GetCurrentStudentIdAsync()
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ErrorException(StatusCodes.Status401Unauthorized,
                    ResponseCodeConstants.UNAUTHORIZED,
                    "User ID not found.");
            }

            IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
            Student? student = await studentRepo.Entities
                .Where(s => s.UserId.ToString() == userId && !s.DeletedAt.HasValue)
                .FirstOrDefaultAsync();

            if (student == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    "Student not found.");
            }

            return student.StudentId;
        }

        /// <summary>
        /// Gets current student user ID
        /// </summary>
        private async Task<Guid> GetCurrentStudentUserIdAsync(Guid studentId)
        {
            IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
            Student? student = await studentRepo.Entities
                .Where(s => s.StudentId == studentId && !s.DeletedAt.HasValue)
                .FirstOrDefaultAsync();

            return student?.UserId ?? Guid.Empty;
        }

        /// <summary>
        /// Validates student has approved retake appeal
        /// </summary>
        private async Task ValidateRetakeRoundAccessAsync(Guid studentUserId, Guid mainRoundId)
        {
            bool hasApprovedRetake = await HasApprovedRetakeAppealAsync(studentUserId, mainRoundId);

            if (!hasApprovedRetake)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    "You do not have permission to access this retake round. An approved appeal for the main round is required.");
            }
        }

        /// <summary>
        /// Validates student has not finished the round
        /// </summary>
        private async Task ValidateStudentNotFinishedAsync(Guid roundId, Guid studentId)
        {
            bool hasFinishedRound = await _configService.IsStudentFinishedRoundAsync(roundId, studentId);

            if (hasFinishedRound)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    "You have already finished this round and cannot access its content anymore.");
            }
        }

        /// <summary>
        /// Validates and marks open code as inputted
        /// </summary>
        private async Task ValidateAndMarkOpenCodeAsync(Guid roundId, Guid studentId, string? openCode)
        {
            bool hasInputtedCode = await _configService.HasStudentInputtedOpenCodeAsync(roundId, studentId);

            if (!hasInputtedCode)
            {
                // Validate open code
                await ValidateOpenCode(roundId, openCode);

                // Mark that student has inputted the code
                await _configService.MarkStudentOpenCodeInputtedAsync(roundId, studentId);
            }
        }

        /// <summary>
        /// Validates pagination parameters
        /// </summary>
        private void ValidatePaginationParameters(int pageNumber, int pageSize)
        {
            if (pageNumber < 1 || pageSize < 1)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Page number and page size must be greater than or equal to 1.");
            }
        }

        /// <summary>
        /// Builds base round query with necessary includes
        /// </summary>
        private IQueryable<Round> BuildBaseRoundQuery(IGenericRepository<Round> roundRepo)
        {
            return roundRepo.Entities
                .Where(r => !r.DeletedAt.HasValue)
                .Include(r => r.Contest)
                .Include(r => r.Problem)
                .Include(r => r.McqTest)
                .Include(r => r.MainRound);
        }

        /// <summary>
        /// Applies search filters to round query
        /// </summary>
        private IQueryable<Round> ApplyRoundSearchFilters(
            IQueryable<Round> query,
            Guid? idSearch,
            Guid? contestIdSearch,
            string? roundNameSearch,
            string? contestNameSearch,
            DateTime? startDate,
            DateTime? endDate)
        {
            if (idSearch.HasValue)
            {
                query = query.Where(r => r.RoundId == idSearch.Value);
            }

            if (contestIdSearch.HasValue)
            {
                query = query.Where(r => r.ContestId == contestIdSearch.Value);
            }

            if (!string.IsNullOrWhiteSpace(roundNameSearch))
            {
                query = query.Where(r => r.Name.Contains(roundNameSearch));
            }

            if (!string.IsNullOrWhiteSpace(contestNameSearch))
            {
                query = query.Where(r => r.Contest.Name.Contains(contestNameSearch));
            }

            if (startDate.HasValue)
            {
                query = query.Where(r => r.Start >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(r => r.End <= endDate.Value);
            }

            return query;
        }

        /// <summary>
        /// Loads time limit configurations for rounds
        /// </summary>
        private async Task<Dictionary<string, Config>> LoadTimeLimitConfigurationsAsync(IReadOnlyCollection<Round> rounds)
        {
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            List<Guid> roundIds = rounds.Select(r => r.RoundId).ToList();
            List<string> tlKeys = roundIds.Select(ConfigKeys.RoundTimeLimitSeconds).ToList();

            List<Config> tlConfigs = await configRepo.Entities
                .Where(c => tlKeys.Contains(c.Key) && c.Scope == SCOPE_CONTEST && c.DeletedAt == null)
                .ToListAsync();

            return tlConfigs.ToDictionary(c => c.Key);
        }

        /// <summary>
        /// Maps round entities to DTOs
        /// </summary>
        private IReadOnlyCollection<GetRoundDTO> MapRoundsToDTO(
            IReadOnlyCollection<Round> rounds,
            Dictionary<string, Config> timeLimitLookup,
            Dictionary<string, Config> mockTestWeightDict)
        {
            return rounds.Select(item =>
            {
                GetRoundDTO roundDTO = _mapper.Map<GetRoundDTO>(item);

                roundDTO.ContestName = item.Contest?.Name ?? "N/A";
                roundDTO.RoundName = item.Name;
                roundDTO.Start = item.Start;
                roundDTO.End = item.End;
                roundDTO.IsRetakeRound = item.IsRetakeRound;
                roundDTO.MainRoundId = item.MainRoundId;

                // Map time limit from config
                string tlKey = ConfigKeys.RoundTimeLimitSeconds(item.RoundId);
                if (timeLimitLookup.TryGetValue(tlKey, out Config? tlConfig) &&
                    int.TryParse(tlConfig.Value, out int secs))
                {
                    roundDTO.TimeLimitSeconds = secs;
                }

                // Map mock test weight from config
                double? mockTestWeight = null;
                if (item.Problem != null
                    && item.Problem.Type == ProblemTypeEnum.AutoEvaluation.ToString()
                    && item.Problem.TestType == AUTO_MOCK_TEST_TEST_TYPE)
                {
                    string weightKey = ConfigKeys.RoundWeight(item.RoundId);
                    if (mockTestWeightDict.TryGetValue(weightKey, out Config? weightConfig)
                        && double.TryParse(weightConfig.Value, out double weight))
                    {
                        mockTestWeight = weight;
                    }
                }

                // Map problem or MCQ test
                MapRoundContent(roundDTO, item, mockTestWeight);

                return roundDTO;
            }).ToList();
        }

        /// <summary>
        /// Loads mock test weight configurations for rounds
        /// </summary>
        private async Task<Dictionary<string, Config>> LoadMockTestWeightConfigurationsAsync(IReadOnlyCollection<Round> rounds)
        {
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            List<Guid> roundIds = rounds.Select(r => r.RoundId).ToList();
            List<string> weightKeys = roundIds.Select(ConfigKeys.RoundWeight).ToList();

            List<Config> weightConfigs = await configRepo.Entities
                .Where(c => weightKeys.Contains(c.Key) && c.Scope == SCOPE_ROUND && c.DeletedAt == null)
                .ToListAsync();

            return weightConfigs.ToDictionary(c => c.Key);
        }

        /// <summary>
        /// Gets round for deletion with related entities
        /// </summary>
        private async Task<Round> GetRoundForDeletionAsync(IGenericRepository<Round> roundRepo, Guid id)
        {
            Round? round = await roundRepo.Entities
                .Where(r => r.RoundId == id)
                .Include(r => r.Problem)
                .Include(r => r.McqTest)
                .FirstOrDefaultAsync();

            if (round == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND, "Round not found.");
            }

            return round;
        }

        /// <summary>
        /// Deletes round content (problem or MCQ test)
        /// </summary>
        private async Task DeleteRoundContentAsync(Round round)
        {
            // Delete related Problem
            if (round.Problem != null && !round.Problem.DeletedAt.HasValue)
            {
                await _problemService.DeleteProblemAsync(round.Problem.ProblemId);
            }

            // Delete related McqTest
            if (round.McqTest != null && !round.McqTest.DeletedAt.HasValue)
            {
                await _mcqTestService.DeleteMcqTestAsync(round.McqTest.TestId);
            }
        }

        /// <summary>
        /// Deletes round configurations (time limit, rank cutoff, distribution status)
        /// </summary>
        private async Task DeleteRoundConfigurationsAsync(Guid roundId)
        {
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            // Delete time limit config
            await DeleteConfigAsync(configRepo, ConfigKeys.RoundTimeLimitSeconds(roundId));

            // Delete rank cutoff config
            await DeleteConfigAsync(configRepo, ConfigKeys.RoundRankCutoff(roundId));

            // Delete distribution status config
            await _configService.ResetDistributionStatusAsync(roundId);
        }

        /// <summary>
        /// Deletes a configuration entry
        /// </summary>
        private async Task DeleteConfigAsync(IGenericRepository<Config> configRepo, string key)
        {
            Config? config = await configRepo.Entities
                .FirstOrDefaultAsync(c => c.Key == key && c.Scope == SCOPE_CONTEST);

            if (config != null)
            {
                config.DeletedAt = DateTime.UtcNow;
                await configRepo.UpdateAsync(config);
            }
        }

        public async Task FastForwardAppealSubmitDeadlineAsync(Guid roundId)
        {
            var configRepo = _unitOfWork.GetRepository<Config>();
            var roundRepo = _unitOfWork.GetRepository<Round>();

            Round? round = await roundRepo.Entities
                .FirstOrDefaultAsync(r => r.RoundId == roundId && r.DeletedAt == null);
            if (round == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Round not found.");

            if (round.IsRetakeRound)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Appeals are not allowed for retake rounds.");

            DateTime now = DateTime.UtcNow;
            if (round.Start > now || round.Status == RoundStatusEnum.Incoming.ToString())
                throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Cannot fast-forward appeal submit before round starts.");

            if (round.End > now)
                throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Appeal submit deadline cannot be before round end.");

            string key = ConfigKeys.RoundAppealSubmitDeadlineUtc(roundId);
            await UpsertDeadlineAsync(configRepo, key, now.AddSeconds(-1), scope: SCOPE_CONTEST);
            await _unitOfWork.SaveAsync();
        }

        public async Task FastForwardAppealReviewDeadlineAsync(Guid roundId)
        {
            var configRepo = _unitOfWork.GetRepository<Config>();
            var roundRepo = _unitOfWork.GetRepository<Round>();

            Round? round = await roundRepo.Entities
                .FirstOrDefaultAsync(r => r.RoundId == roundId && r.DeletedAt == null);
            if (round == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Round not found.");

            if (round.IsRetakeRound)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Appeals are not allowed for retake rounds.");

            DateTime now = DateTime.UtcNow;
            if (round.Start > now || round.Status == RoundStatusEnum.Incoming.ToString())
                throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Cannot fast-forward appeal review before round starts.");

            int submitDays = await GetContestPolicyDaysAsync(
                round.ContestId, ContestPolicyKeys.AppealSubmitDays, DEFAULT_APPEAL_SUBMIT_DAYS, configRepo);
            int reviewDays = await GetContestPolicyDaysAsync(
                round.ContestId, ContestPolicyKeys.AppealReviewDays, DEFAULT_APPEAL_REVIEW_DAYS, configRepo);

            // Ensure appeal review is not before appeal submit
            var (submitDeadline, _) = await GetAppealDeadlinesUtcAsync(round, configRepo, submitDays, reviewDays);
            if (submitDeadline > now)
                throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Appeal review cannot end before appeal submit deadline.");

            string key = ConfigKeys.RoundAppealReviewDeadlineUtc(roundId);
            await UpsertDeadlineAsync(configRepo, key, now.AddSeconds(-1), scope: SCOPE_CONTEST);
            await _unitOfWork.SaveAsync();
        }

        public async Task FastForwardJudgeDeadlineAsync(Guid roundId)
        {
            var configRepo = _unitOfWork.GetRepository<Config>();
            var submissionRepo = _unitOfWork.GetRepository<Submission>();
            var roundRepo = _unitOfWork.GetRepository<Round>();

            Round? round = await roundRepo.Entities
                .Include(r => r.Problem)
                .FirstOrDefaultAsync(r => r.RoundId == roundId && r.DeletedAt == null);

            if (round == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Round not found.");

            if (round.Problem?.Type != ProblemTypeEnum.Manual.ToString())
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Judge deadline applies only to manual rounds.");

            DateTime now = DateTime.UtcNow;
            if (round.End > now)
                throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Judge deadline cannot be before round end.");

            int submitDays = await GetContestPolicyDaysAsync(
                round.ContestId, ContestPolicyKeys.AppealSubmitDays, DEFAULT_APPEAL_SUBMIT_DAYS, configRepo);
            int reviewDays = await GetContestPolicyDaysAsync(
                round.ContestId, ContestPolicyKeys.AppealReviewDays, DEFAULT_APPEAL_REVIEW_DAYS, configRepo);

            var (appealSubmitDeadline, appealReviewDeadline) = await GetAppealDeadlinesUtcAsync(
                round, configRepo, submitDays, reviewDays);

            List<Submission> subs = await submissionRepo.Entities
                .Where(s => s.Problem != null && s.Problem.RoundId == roundId && s.DeletedAt == null)
                .ToListAsync();

            DateTime past = now.AddSeconds(-1);

            if (now >= appealReviewDeadline)
            {
                string judgeRescoreKey = ConfigKeys.RoundJudgeRescoreDeadlineUtc(roundId);
                await UpsertDeadlineAsync(configRepo, judgeRescoreKey, past, scope: SCOPE_CONTEST);
            }
            else if (now <= appealSubmitDeadline)
            {
                string judgeKey = ConfigKeys.RoundJudgeDeadlineUtc(roundId);
                await UpsertDeadlineAsync(configRepo, judgeKey, past, scope: SCOPE_CONTEST);
            }
            else
            {
                throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE",
                    "Judge deadline can only be fast-forwarded before appeal submit ends or after appeal review ends.");
            }

            foreach (var sub in subs)
            {
                if (Guid.TryParse(sub.JudgedBy, out var judgeId))
                {
                    string key = ConfigKeys.JudgeSubmissionDeadline(judgeId, sub.SubmissionId);
                    await UpsertDeadlineAsync(configRepo, key, past, scope: SCOPE_CONTEST);
                }
            }

            await _unitOfWork.SaveAsync();
        }

        public async Task TryFinalizeRoundAsync(Guid roundId)
        {
            var roundRepo = _unitOfWork.GetRepository<Round>();
            Round? round = await roundRepo.Entities
                .Include(r => r.Problem)
                .Include(r => r.McqTest)
                .FirstOrDefaultAsync(r => r.RoundId == roundId && r.DeletedAt == null);

            if (round == null) return;

            DateTime finalizeNotBefore = await GetFinalizeNotBeforeAsync(roundId);

            // Finalize only when all deadlines and round end are in the past
            if (DateTime.UtcNow < finalizeNotBefore)
                return;

            await RoundFinalizer.TryFinalizeAsync(_unitOfWork, roundId);
        }

        public async Task<DateTime> GetFinalizeNotBeforeAsync(Guid roundId)
        {
            var roundRepo = _unitOfWork.GetRepository<Round>();
            Round? round = await roundRepo.Entities
                .Include(r => r.Problem)
                .Include(r => r.McqTest)
                .FirstOrDefaultAsync(r => r.RoundId == roundId && r.DeletedAt == null);

            if (round == null)
                return DateTime.UtcNow;

            var configRepo = _unitOfWork.GetRepository<Config>();
            int judgeDays = await GetContestPolicyDaysAsync(
                round.ContestId, ContestPolicyKeys.JudgeRescoreDays, DEFAULT_JUDGE_RESCORE_DAYS, configRepo);
            int submitDays = await GetContestPolicyDaysAsync(
                round.ContestId, ContestPolicyKeys.AppealSubmitDays, DEFAULT_APPEAL_SUBMIT_DAYS, configRepo);
            int reviewDays = await GetContestPolicyDaysAsync(
                round.ContestId, ContestPolicyKeys.AppealReviewDays, DEFAULT_APPEAL_REVIEW_DAYS, configRepo);

            bool isManual = IsManualRound(round);

            var (_, reviewDeadline) = await GetAppealDeadlinesUtcAsync(round, configRepo, submitDays, reviewDays);

            if (round.IsRetakeRound)
            {
                if (!isManual)
                    return round.End.ToUniversalTime();

                var (judgeDeadline, _) = await GetJudgeDeadlinesUtcAsync(
                    round, configRepo, judgeDays, submitDays, reviewDays);
                return judgeDeadline;
            }

            if (!isManual)
            {
                return reviewDeadline;
            }

            // Manual main round: need appeal review AND judge windows (initial + rescore)
            var (initialJudgeDeadline, rescoreDeadline) = await GetJudgeDeadlinesUtcAsync(
                round, configRepo, judgeDays, submitDays, reviewDays);

            // Finalize not-before should be after appeal review window AND after judge windows
            return new[] { reviewDeadline, initialJudgeDeadline, rescoreDeadline }.Max();
        }

        public async Task<RoundTimelineDTO> GetRoundTimelineAsync(Guid roundId)
        {
            Round round = await FetchRoundWithIncludesAsync(roundId);
            var configRepo = _unitOfWork.GetRepository<Config>();

            int submitDays = await GetContestPolicyDaysAsync(
                round.ContestId, ContestPolicyKeys.AppealSubmitDays, DEFAULT_APPEAL_SUBMIT_DAYS, configRepo);
            int reviewDays = await GetContestPolicyDaysAsync(
                round.ContestId, ContestPolicyKeys.AppealReviewDays, DEFAULT_APPEAL_REVIEW_DAYS, configRepo);
            int judgeDays = await GetContestPolicyDaysAsync(
                round.ContestId, ContestPolicyKeys.JudgeRescoreDays, DEFAULT_JUDGE_RESCORE_DAYS, configRepo);

            DateTime? appealSubmitDeadline = null;
            DateTime? appealReviewDeadline = null;
            if (!round.IsRetakeRound)
            {
                var (submitDeadline, reviewDeadline) = await GetAppealDeadlinesUtcAsync(
                    round, configRepo, submitDays, reviewDays);
                appealSubmitDeadline = submitDeadline;
                appealReviewDeadline = reviewDeadline;
            }

            DateTime? judgeDeadline = null;
            DateTime? judgeRescoreDeadline = null;
            if (IsManualRound(round))
            {
                var (initialJudgeDeadline, rescoreDeadline) = await GetJudgeDeadlinesUtcAsync(
                    round, configRepo, judgeDays, submitDays, reviewDays);
                judgeDeadline = initialJudgeDeadline;
                judgeRescoreDeadline = rescoreDeadline;
            }

            return new RoundTimelineDTO
            {
                RoundId = roundId,
                Start = round.Start.ToUniversalTime(),
                End = round.End.ToUniversalTime(),
                AppealSubmitDeadline = appealSubmitDeadline,
                AppealReviewDeadline = appealReviewDeadline,
                JudgeDeadline = judgeDeadline,
                JudgeRescoreDeadline = judgeRescoreDeadline
            };
        }

        private static async Task UpsertDeadlineAsync(IGenericRepository<Config> configRepo, string key, DateTime value, string scope)
        {
            Config? existing = await configRepo.Entities.FirstOrDefaultAsync(c => c.Key == key && c.DeletedAt == null);
            string iso = value.ToString("o");
            if (existing == null)
            {
                await configRepo.InsertAsync(new Config
                {
                    Key = key,
                    Value = iso,
                    Scope = scope,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.Value = iso;
                existing.Scope = scope;
                existing.UpdatedAt = DateTime.UtcNow;
                await configRepo.UpdateAsync(existing);
            }
        }

        private static DateTime ParseOrDefault(string? isoString, DateTime fallback)
        {
            return DateTime.TryParse(
                isoString,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime parsed)
                ? parsed
                : fallback;
        }

        private static DateTime? TryParseUtc(string? isoString)
        {
            if (string.IsNullOrWhiteSpace(isoString))
                return null;

            if (!DateTime.TryParse(
                    isoString,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTime parsed))
            {
                return null;
            }

            return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        }

        private static async Task<DateTime?> TryGetDeadlineUtcAsync(
            IGenericRepository<Config> configRepo,
            string key)
        {
            string? value = await configRepo.Entities
                .Where(c => c.Key == key && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            return TryParseUtc(value);
        }

        private async Task<(DateTime SubmitDeadline, DateTime ReviewDeadline)> GetAppealDeadlinesUtcAsync(
            Round round,
            IGenericRepository<Config> configRepo,
            int submitDays,
            int reviewDays)
        {
            DateTime submitDeadline = round.End.AddDays(submitDays).ToUniversalTime();
            DateTime reviewDeadline = submitDeadline.AddDays(reviewDays).ToUniversalTime();

            DateTime? submitOverride = await TryGetDeadlineUtcAsync(
                configRepo, ConfigKeys.RoundAppealSubmitDeadlineUtc(round.RoundId));
            if (submitOverride.HasValue)
            {
                submitDeadline = submitOverride.Value;
            }

            DateTime? reviewOverride = await TryGetDeadlineUtcAsync(
                configRepo, ConfigKeys.RoundAppealReviewDeadlineUtc(round.RoundId));
            if (reviewOverride.HasValue)
            {
                reviewDeadline = reviewOverride.Value;
            }

            return (submitDeadline, reviewDeadline);
        }

        private async Task<(DateTime JudgeDeadline, DateTime JudgeRescoreDeadline)> GetJudgeDeadlinesUtcAsync(
            Round round,
            IGenericRepository<Config> configRepo,
            int judgeDays,
            int submitDays,
            int reviewDays)
        {
            DateTime judgeDeadline = round.End.AddDays(judgeDays).ToUniversalTime();
            DateTime judgeRescoreDeadline = round.End.AddDays(judgeDays * 2 + submitDays + reviewDays).ToUniversalTime();

            DateTime? judgeOverride = await TryGetDeadlineUtcAsync(
                configRepo, ConfigKeys.RoundJudgeDeadlineUtc(round.RoundId));
            if (judgeOverride.HasValue)
            {
                judgeDeadline = judgeOverride.Value;
            }

            DateTime? rescoreOverride = await TryGetDeadlineUtcAsync(
                configRepo, ConfigKeys.RoundJudgeRescoreDeadlineUtc(round.RoundId));
            if (rescoreOverride.HasValue)
            {
                judgeRescoreDeadline = rescoreOverride.Value;
            }

            return (judgeDeadline, judgeRescoreDeadline);
        }

        /// <summary>
        /// Performs post-deletion operations (logging and notifications)
        /// </summary>
        private async Task PerformPostDeleteRoundOperationsAsync(Guid roundId, Guid contestId, string roundName)
        {
            var actorId = GetCurrentUserGuidOrThrow();

            await SafeWriteActivityAsync(actorId,
                ActivityActions.RoundDelete,
                TargetTypes.Round,
                roundId.ToString());

            await SafeNotifyContestParticipantsAsync(contestId,
                NotificationTypes.RoundDeleted,
                new
                {
                    contestId = contestId,
                    roundId = roundId,
                    name = roundName,
                    targetType = TargetTypes.Round,
                    targetId = roundId.ToString(),
                    message = $"Round '{roundName}' has deleted."
                });
        }

    }
}
