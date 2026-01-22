using AutoMapper;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.Dashboards;
using BusinessLogic.IServices.FileStorages;
using BusinessLogic.IServices.NotificationsAndLogs;
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
using System;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.Helpers;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Contests
{
    public class ContestService : IContestService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly INotificationService _notificationService;
        private readonly IActivityLogWriter _activityLogWriter;
        private readonly ILogger<ContestService> _logger;
        private readonly IRoundService _roundService;
        private readonly IDashboardNotifierService _dashboardNotifier;

        private const int MIN_YEAR = 10;
        private const string CONTEST_IMAGE_FOLDER = "contest_images";
        private const string CONTEST_REPORT_FOLDER = "contest_reports";
        private const string CONTEST_REPORT_ATTACHEMENT_TYPE = "contest_report";
        private const string CSV_NEW_LINE = "\r\n";
        private const char CSV_DELIMITER = ';';

        private const string SUBMISSION_STATUS_CANCELLED = nameof(SubmissionStatusEnum.Cancelled);
        private const string MCQ_ATTEMPT_STATUS_CANCELLED = nameof(McqAttemptStatusEnum.Cancelled);
        private const string PROBLEM_TYPE_AUTO_EVALUATION = nameof(ProblemTypeEnum.AutoEvaluation);
        private const string PROBLEM_TYPE_MANUAL = nameof(ProblemTypeEnum.Manual);
        private const string PROBLEM_TYPE_MCQ_TEST = nameof(ProblemTypeEnum.McqTest);
        private const string AUTO_MOCK_TEST_TEST_TYPE = nameof(TestTypeEnum.MockTest);
        private const string AUTO_INPUT_OUTPUT_TEST_TYPE = nameof(TestTypeEnum.InputOutput);

        private const string ROUND_TYPE_MCQ_TEST = "MCQ Test";
        private const string ROUND_TYPE_AUTO_EVALUATION = "Auto Evaluation";
        private const string UNKNOWN_ORGANIZER = "Unknown Organizer";
        private const string UNKNOWN_ROUND = "Unknown Round";
        private const string TIME_LIMIT_SECONDS_KEY_SUFFIX = "time_limit_seconds";
        private const string MOCK_TEST_WEIGHT_KEY_SUFFIX = "weight";
        private const int DEFAULT_APPEAL_SUBMIT_DAYS = 2;
        private const int DEFAULT_APPEAL_REVIEW_DAYS = 1;
        private const int DEFAULT_JUDGE_RESCORE_DAYS = 1;

        public ContestService(
            IMapper mapper,
            IUOW uow,
            IHttpContextAccessor httpContextAccessor,
            ICloudinaryService cloudinaryService,
            INotificationService notificationService,
            IActivityLogWriter activityLogWriter,
            ILogger<ContestService> logger,
            IRoundService roundService,
            IDashboardNotifierService dashboardNotifier)
        {
            _mapper = mapper;
            _unitOfWork = uow;
            _httpContextAccessor = httpContextAccessor;
            _cloudinaryService = cloudinaryService;
            _notificationService = notificationService;
            _activityLogWriter = activityLogWriter;
            _logger = logger;
            _roundService = roundService;
            _dashboardNotifier = dashboardNotifier;
        }

        public async Task DeleteContestAsync(Guid id)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Validate contest ID
                if (id == Guid.Empty)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid contest ID.");
                }

                // Get repository and fetch the contest by ID
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                Contest? existingContest = await contestRepo.GetByIdAsync(id);

                // Check if the contest exists
                if (existingContest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");
                }

                // Check if the contest is already deleted
                if (existingContest.DeletedAt.HasValue)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Contest already deleted.");
                }

                // Soft delete
                existingContest.DeletedAt = DateTime.UtcNow;

                // Set status to Cancelled
                existingContest.Status = ContestStatusEnum.Cancelled.ToString();

                // Update the contest
                await contestRepo.UpdateAsync(existingContest);

                // Save changes to database
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();

                // Log contest cancellation activity
                Guid actorId = GetCurrentUserGuidOrThrow();
                await SafeWriteActivityAsync(actorId, ActivityActions.ContestCancel, TargetTypes.Contest, id.ToString());

                // Notify participants about contest cancellation
                await SafeNotifyParticipantsAsync(id, NotificationTypes.ContestCancelled, new
                {
                    contestId = id,
                    targetType = TargetTypes.Contest,
                    targetId = id.ToString(),
                    message = "This contest has been cancelled."
                });

                await _dashboardNotifier.NotifyContestStatusChangedAsync();

                // Get current user
                string currentUserId = GetCurrentUserIdOrThrow();

                Guid organizerId = Guid.Parse(currentUserId);

                await _dashboardNotifier.NotifyOrganizerDashboardUpdatedAsync(organizerId);

            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error deleting Contests: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetContestDTO>> GetPaginatedContestAsync(
            int pageNumber,
            int pageSize,
            Guid? idSearch,
            Guid? creatorIdSearch,
            Guid? roundIdSearch,
            string? nameSearch,
            int? yearSearch,
            DateTime? startDate,
            DateTime? endDate,
            bool isMyParticipatedContest,
            bool isMyContest)
        {
            try
            {
                // Validate all input parameters
                ValidatePaginationParameters(pageNumber, pageSize);
                ValidateYearSearch(yearSearch);
                ValidateDateRange(startDate, endDate);

                // Get contest repository
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

                // Build base query with necessary includes
                IQueryable<Contest> query = BuildBaseContestQuery(contestRepo);

                // Get user context
                string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                string? userRole = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role);

                // Apply participation filters
                if (isMyParticipatedContest)
                {
                    query = await ApplyParticipantFilterAsync(query, userRole, userId);
                }

                // Apply ownership filter
                if (isMyContest && !string.IsNullOrEmpty(userId))
                {
                    query = query.Where(c => c.CreatedBy == userId);
                }
                else
                {
                    // Exclude draft contests for non-owners
                    query = query.Where(c => c.Status != ContestStatusEnum.Draft.ToString());
                }

                // Apply search filters
                query = ApplySearchFilters(query, idSearch, creatorIdSearch, roundIdSearch, nameSearch, yearSearch, startDate, endDate);

                // Order and paginate
                query = query.OrderByDescending(c => c.CreatedAt);
                PaginatedList<Contest> resultQuery = await contestRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Load related data efficiently
                var (configLookup, organizerNames, timeLimitDict, mockTestWeightDict) = await LoadRelatedDataAsync(resultQuery.Items);

                // Map entities to DTOs
                IReadOnlyCollection<GetContestDTO> result = resultQuery.Items
                    .Select(item => MapContestEntityToDTO(item, configLookup, organizerNames, timeLimitDict, mockTestWeightDict))
                    .ToList();

                // Return paginated result
                return new PaginatedList<GetContestDTO>(result, resultQuery.TotalCount, resultQuery.PageNumber, resultQuery.PageSize);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving Contests: {ex.Message}");
            }
        }

        public async Task<GetContestDTO> GetContestByIdAsync(Guid id)
        {
            try
            {
                // Get contest repository
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

                // Fetch contest with related data
                Contest contest = await FetchContestWithIncludesAsync(contestRepo, id);

                // Load related data
                var (configLookup, organizerName, timeLimitDict, mockTestWeightDict) = await LoadContestRelatedDataAsync(contest);

                // Map to DTO
                return MapSingleContestToDTO(contest, configLookup, organizerName, timeLimitDict, mockTestWeightDict);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving Contests: {ex.Message}");
            }
        }

        public async Task<GetContestDTO> UpdateContestAsync(Guid id, UpdateContestDTO contestDTO)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                // Validate input
                ValidateUpdateContestInput(id, contestDTO);

                // Get repositories
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Get and validate existing contest
                Contest existingContest = await GetExistingContestOrThrowAsync(contestRepo, id);

                // Check for duplicate name
                await CheckDuplicateContestNameAsync(contestRepo, contestDTO.Name!, contestDTO.Year, existingContest.Name);

                // Store old values for notification
                var oldValues = CaptureOldContestValues(existingContest);

                // Update contest entity
                await UpdateContestEntityAsync(existingContest, contestDTO, configRepo);

                // Save changes
                await contestRepo.UpdateAsync(existingContest);
                await _unitOfWork.SaveAsync();
                _unitOfWork.CommitTransaction();

                // Post-update operations (logging, notifications, scheduling)
                await PerformPostUpdateOperationsAsync(existingContest, oldValues);

                // Notify organizer dashboard
                Guid organizerId = Guid.Parse(GetCurrentUserIdOrThrow());
                await _dashboardNotifier.NotifyOrganizerDashboardUpdatedAsync(organizerId);

                // Notify mentors dashboard
                await NotifiMentorDashboardContestUpdated(existingContest.ContestId);

                // Return updated contest
                PaginatedList<GetContestDTO> result = await GetPaginatedContestAsync(
                    1, 1, existingContest.ContestId, null, null, null, null, null, null, false, false);

                return result.Items.First();
            }
            catch (Exception ex)
            {
                _unitOfWork.RollBack();

                if (ex is ErrorException || ex is CoreException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error updating Contests: {ex.Message}");
            }
        }

        public async Task<ContestCreatedDTO> CreateContestAsync(CreateContestAdvancedDTO dto)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                // Validate input
                ValidateCreateContestInput(dto);

                // Get current user
                string currentUserId = GetCurrentUserIdOrThrow();

                // Get repositories
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Check for duplicate name
                string nameTrim = dto.Name!.Trim();
                await CheckDuplicateContestNameForNewAsync(contestRepo, nameTrim, dto.Year);

                // Upload image if provided
                string imageUrl = await UploadImageIfProvidedAsync(dto.ImageFile);

                // Create contest entity
                Contest entity = CreateContestEntity(dto, currentUserId, nameTrim, imageUrl);

                // Insert contest
                await contestRepo.InsertAsync(entity);
                await _unitOfWork.SaveAsync();

                // Configure contest settings
                var configValues = await ConfigureNewContestAsync(entity.ContestId, dto, configRepo);
                var policyValues = await ConfigureContestPoliciesAsync(entity.ContestId, dto.AppealSubmitDays, dto.AppealReviewDays, dto.JudgeRescoreDays, configRepo);

                // Save configurations
                await _unitOfWork.SaveAsync();
                _unitOfWork.CommitTransaction();

                // Post-create operations
                await PerformPostCreateOperationsAsync(entity);

                // Notify dashboard
                await _dashboardNotifier.NotifyContestCreatedAsync();

                Guid organizerId = Guid.Parse(currentUserId);

                await _dashboardNotifier.NotifyOrganizerDashboardUpdatedAsync(organizerId);

                // Map and return result
                return MapToContestCreatedDTO(entity, dto, imageUrl, configValues, policyValues);
            }
            catch (Exception ex)
            {
                _unitOfWork.RollBack();

                if (ex is ErrorException || ex is CoreException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating Contests: {ex.Message}");
            }
        }

        public async Task<PublishReadinessDTO> CheckPublishReadinessAsync(Guid contestId)
        {
            // Get repositories
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            // Fetch the contest with its rounds
            Contest? contest = await contestRepo.Entities
                .Where(c => c.ContestId == contestId && c.DeletedAt == null)
                .Include(c => c.Rounds)
                    .ThenInclude(r => r.Problem)
                        .ThenInclude(p => p!.TestCases)
                .FirstOrDefaultAsync();

            // Validate contest existence
            if (contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

            // Initialize result DTO
            PublishReadinessDTO result = new PublishReadinessDTO { ContestId = contestId };

            // Get non-deleted rounds
            List<Round> rounds = contest.Rounds.Where(r => !r.DeletedAt.HasValue).ToList();

            // Check if there are any rounds
            if (!rounds.Any())
            {
                result.Missing.Add("No rounds found.");
                result.IsReady = false;
                return result;
            }

            // Extract round IDs
            List<Guid> roundIds = rounds.Select(r => r.RoundId).ToList();

            await ValidateContestEndAgainstDeadlinesAsync(contest, rounds, configRepo, result);

            // Validate time limits
            await ValidateRoundTimeLimitsAsync(configRepo, rounds, roundIds, result);

            // Get problems and MCQ tests
            IGenericRepository<Problem> problemRepo = _unitOfWork.GetRepository<Problem>();
            IGenericRepository<McqTest> mcqTestRepo = _unitOfWork.GetRepository<McqTest>();

            List<Problem> problems = await problemRepo.Entities
                .Where(p => roundIds.Contains(p.RoundId) && p.DeletedAt == null)
                .Include(p => p.TestCases)
                .ToListAsync();

            List<McqTest> mcqTests = await mcqTestRepo.Entities
                .Where(t => roundIds.Contains(t.RoundId) && t.DeletedAt == null)
                .Include(t => t.Round)
                .Include(t => t.McqTestQuestions)
                .ToListAsync();

            // Validate retake round weights
            await ValidateRetakeRoundWeightsAsync(rounds, problems, mcqTests, result);

            // Validate round content
            ValidateRoundContent(rounds, problems, mcqTests, result);

            // Validate MCQ tests
            ValidateMcqTests(mcqTests, result);

            // Validate auto-evaluation problems
            ValidateAutoEvaluationProblems(problems, rounds, result);

            // Validate manual problems and judges
            await ValidateManualProblemsAsync(contestId, configRepo, problems, rounds, result);

            // Validate contest configuration
            await ValidateContestConfigurationAsync(contestId, configRepo, result);

            // Validate contest image
            if (string.IsNullOrWhiteSpace(contest.ImgUrl))
            {
                result.Missing.Add("Contest image not uploaded.");
            }

            // Final readiness determination
            result.IsReady = result.Missing.Count == 0;
            return result;
        }

        private static async Task ValidateRoundTimeLimitsAsync(
            IGenericRepository<Config> configRepo,
            List<Round> rounds,
            List<Guid> roundIds,
            PublishReadinessDTO result)
        {
            // Check for time limit configuration on all rounds
            List<Config> timeLimitConfigs = await configRepo.Entities
                .Where(c => roundIds.Any(rid => c.Key.Contains(rid.ToString()))
                            && c.Key.Contains("time_limit_seconds")
                            && c.DeletedAt == null)
                .ToListAsync();

            // Create a set of round IDs that have time limit configured
            HashSet<Guid> roundsWithTimeLimit = new HashSet<Guid>();
            foreach (Config config in timeLimitConfigs)
            {
                if (!string.IsNullOrEmpty(config.Value) && int.TryParse(config.Value, out int timeLimit))
                {
                    foreach (Guid roundId in roundIds)
                    {
                        if (config.Key.Contains(roundId.ToString()))
                        {
                            roundsWithTimeLimit.Add(roundId);
                            break;
                        }
                    }
                }
            }

            // Find rounds without time limit
            List<Round> roundsWithoutTimeLimit = rounds
                .Where(r => !roundsWithTimeLimit.Contains(r.RoundId))
                .ToList();

            if (roundsWithoutTimeLimit.Any())
            {
                string roundNames = string.Join(", ", roundsWithoutTimeLimit.Select(r => $"'{r.Name}'"));
                result.Missing.Add($"Round(s) {roundNames} missing time limit configuration.");
            }
        }

        private async Task ValidateContestEndAgainstDeadlinesAsync(
            Contest contest,
            List<Round> rounds,
            IGenericRepository<Config> configRepo,
            PublishReadinessDTO result)
        {
            if (!contest.End.HasValue)
                return;

            Round lastRound = rounds.OrderBy(r => r.End).Last();

            // Use actual finalize-not-before (considers configured deadlines/time-travel)
            DateTime requiredEnd = await _roundService.GetFinalizeNotBeforeAsync(lastRound.RoundId);

            if (contest.End.Value < requiredEnd)
            {
                result.Missing.Add(
                    $"Contest end is too early to cover deadlines. Suggested end >= {requiredEnd:yyyy-MM-dd HH:mm:ss} UTC.");
            }
        }

        private async Task ValidateRetakeRoundWeightsAsync(
            List<Round> rounds,
            List<Problem> problems,
            List<McqTest> mcqTests,
            PublishReadinessDTO result)
        {
            List<Round> retakeRounds = rounds.Where(r => r.IsRetakeRound && r.MainRoundId.HasValue).ToList();
            if (!retakeRounds.Any())
                return;

            // Get repositories for weight calculation
            IGenericRepository<McqTestQuestion> mcqTestQuestionRepo = _unitOfWork.GetRepository<McqTestQuestion>();
            IGenericRepository<TestCase> testCaseRepo = _unitOfWork.GetRepository<TestCase>();

            // Load all weights
            List<Guid> mcqTestIds = mcqTests.Select(t => t.TestId).ToList();
            List<McqTestQuestion> allMcqTestQuestions = await mcqTestQuestionRepo.Entities
                .Where(q => mcqTestIds.Contains(q.TestId))
                .ToListAsync();

            List<Guid> problemIds = problems.Select(p => p.ProblemId).ToList();
            List<TestCase> allTestCases = await testCaseRepo.Entities
                .Where(tc => problemIds.Contains(tc.ProblemId) && tc.DeletedAt == null)
                .ToListAsync();

            // Calculate weights for each round
            Dictionary<Guid, double> roundWeights = CalculateRoundWeights(rounds, problems, mcqTests, allMcqTestQuestions, allTestCases);

            // Validate each retake round against its main round
            foreach (Round retakeRound in retakeRounds)
            {
                if (!retakeRound.MainRoundId.HasValue)
                    continue;

                double retakeWeight = roundWeights.GetValueOrDefault(retakeRound.RoundId, 0);
                double mainWeight = roundWeights.GetValueOrDefault(retakeRound.MainRoundId.Value, 0);

                Round? mainRound = rounds.FirstOrDefault(r => r.RoundId == retakeRound.MainRoundId);
                string mainRoundName = mainRound?.Name ?? "Unknown";

                // Validate weights
                if (retakeWeight <= 0)
                {
                    result.Missing.Add($"Retake round '{retakeRound.Name}' has no weight (missing questions/test cases/criteria).");
                }
                else if (mainWeight <= 0)
                {
                    result.Missing.Add($"Main round '{mainRoundName}' for retake round '{retakeRound.Name}' has no weight (missing questions/test cases/criteria).");
                }
                else if (Math.Abs(retakeWeight - mainWeight) > 0.0001)
                {
                    result.Missing.Add($"Retake round '{retakeRound.Name}' total weight ({retakeWeight}) does not match its main round '{mainRoundName}' weight ({mainWeight}).");
                }
            }
        }

        private static Dictionary<Guid, double> CalculateRoundWeights(
            List<Round> rounds,
            List<Problem> problems,
            List<McqTest> mcqTests,
            List<McqTestQuestion> allMcqTestQuestions,
            List<TestCase> allTestCases)
        {
            Dictionary<Guid, double> roundWeights = new Dictionary<Guid, double>();

            foreach (Round round in rounds)
            {
                double totalWeight = 0;

                // Check if round has MCQ test
                McqTest? mcqTest = mcqTests.FirstOrDefault(t => t.RoundId == round.RoundId);
                if (mcqTest != null)
                {
                    totalWeight = allMcqTestQuestions
                        .Where(q => q.TestId == mcqTest.TestId)
                        .Sum(q => q.Weight);
                }
                else
                {
                    // Check if round has problem (auto-evaluation or manual)
                    Problem? problem = problems.FirstOrDefault(p => p.RoundId == round.RoundId);
                    if (problem != null)
                    {
                        totalWeight = allTestCases
                            .Where(tc => tc.ProblemId == problem.ProblemId)
                            .Sum(tc => tc.Weight);
                    }
                }

                roundWeights[round.RoundId] = totalWeight;
            }

            return roundWeights;
        }

        private static void ValidateRoundContent(
            List<Round> rounds,
            List<Problem> problems,
            List<McqTest> mcqTests,
            PublishReadinessDTO result)
        {
            HashSet<Guid> roundsWithProblems = problems.Select(p => p.RoundId).ToHashSet();
            HashSet<Guid> roundsWithMcqTests = mcqTests.Select(t => t.RoundId).ToHashSet();
            HashSet<Guid> roundsWithContent = roundsWithProblems.Union(roundsWithMcqTests).ToHashSet();

            List<Round> roundsWithoutContent = rounds
                .Where(r => !roundsWithContent.Contains(r.RoundId))
                .ToList();

            if (roundsWithoutContent.Any())
            {
                string roundNames = string.Join(", ", roundsWithoutContent.Select(r => $"'{r.Name}'"));
                result.Missing.Add($"Round(s) {roundNames} missing a problem or MCQ test.");
            }
        }

        private static void ValidateMcqTests(List<McqTest> mcqTests, PublishReadinessDTO result)
        {
            List<McqTest> mcqTestsWithoutQuestions = mcqTests
                .Where(t => !t.McqTestQuestions.Any())
                .ToList();

            if (mcqTestsWithoutQuestions.Any())
            {
                string testInfo = string.Join(", ", mcqTestsWithoutQuestions.Select(t =>
                    $"'{t.Round.Name}'"));
                result.Missing.Add($"MCQ test(s) {testInfo} have no questions.");
            }
        }

        private static void ValidateAutoEvaluationProblems(
    List<Problem> problems,
    List<Round> rounds,
    PublishReadinessDTO result)
        {
            // Filter auto-evaluation problems
            List<Problem> autoEvalProblems = problems
                .Where(p => p.Type == ProblemTypeEnum.AutoEvaluation.ToString())
                .ToList();

            // Validate each auto-evaluation problem
            foreach (Problem problem in autoEvalProblems)
            {
                Round? round = rounds.FirstOrDefault(r => r.RoundId == problem.RoundId);
                string roundName = round?.Name ?? "Unknown Round";

                // Check for template URL
                bool hasTemplateUrl = !string.IsNullOrWhiteSpace(problem.TemplateUrl);
                if (!hasTemplateUrl)
                {
                    result.Missing.Add($"Auto-evaluation round '{roundName}' is missing student code template.");
                }

                // Check for mock test URL and test cases
                bool hasMockTestUrl = !string.IsNullOrWhiteSpace(problem.MockTestUrl);
                bool hasTestCases = problem.TestCases.Any(tc => tc.DeletedAt == null);

                if (!hasMockTestUrl && !hasTestCases)
                {
                    result.Missing.Add($"Auto-evaluation round '{roundName}' must have either a mock test file or test cases.");
                }
                else if (hasMockTestUrl && hasTestCases)
                {
                    result.Missing.Add($"Auto-evaluation round '{roundName}' cannot have both mock test file and test cases. Please use only one evaluation method.");
                }

                // Check mock test round's weight
                if (problem.TestType == AUTO_MOCK_TEST_TEST_TYPE)
                {
                    string mockTestRoundWeightKey = ConfigKeys.RoundWeight(round!.RoundId);

                    if (string.IsNullOrWhiteSpace(mockTestRoundWeightKey))
                    {
                        result.Missing.Add($"Auto-evaluation round '{roundName}' with mock test type is missing weight.");
                    }
                }
            }
        }

        private static async Task ValidateManualProblemsAsync(
            Guid contestId,
            IGenericRepository<Config> configRepo,
            List<Problem> problems,
            List<Round> rounds,
            PublishReadinessDTO result)
        {
            List<Problem> manualProblems = problems
                .Where(p => p.Type == ProblemTypeEnum.Manual.ToString())
                .ToList();

            if (!manualProblems.Any())
                return;

            // Check for rubrics (test cases)
            List<Problem> manualProblemsWithoutRubrics = manualProblems
                .Where(p => !p.TestCases.Any(tc => tc.DeletedAt == null))
                .ToList();

            if (manualProblemsWithoutRubrics.Any())
            {
                string problemInfo = string.Join(", ", manualProblemsWithoutRubrics.Select(p =>
                {
                    Round? round = rounds.FirstOrDefault(r => r.RoundId == p.RoundId);
                    return $"'{round?.Name ?? "Unknown Round"}'";
                }));
                result.Missing.Add($"Manual evaluation round(s) {problemInfo} missing rubric.");
            }

            // Check for judges
            string contestJudgeKey = $"contest:{contestId}:judge:";
            int judgeCount = await configRepo.Entities
                .Where(c => c.Key.StartsWith(contestJudgeKey) && c.DeletedAt == null)
                .CountAsync();

            if (judgeCount == 0)
            {
                result.Missing.Add("No judges assigned to the contest for manual problems.");
            }
        }

        private static async Task ValidateContestConfigurationAsync(
            Guid contestId,
            IGenericRepository<Config> configRepo,
            PublishReadinessDTO result)
        {
            // Check registration window
            string? regStart = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestRegStart(contestId) && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();
            string? regEnd = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestRegEnd(contestId) && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(regStart) || string.IsNullOrEmpty(regEnd))
                result.Missing.Add("Registration window not configured.");

            // Check team members configuration
            string? membersMaxContest = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestTeamMembersMax(contestId) && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();
            string? membersMaxDefault = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.Defaults_TeamMembersMax && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(membersMaxContest) && string.IsNullOrEmpty(membersMaxDefault))
                result.Missing.Add("Team members max not configured (contest or global).");

            string? membersMinContest = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.ContestTeamMembersMin(contestId) && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();
            string? membersMinDefault = await configRepo.Entities
                .Where(c => c.Key == ConfigKeys.Defaults_TeamMembersMin && c.DeletedAt == null)
                .Select(c => c.Value).FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(membersMinContest) && string.IsNullOrEmpty(membersMinDefault))
                result.Missing.Add("Team members min not configured (contest or global).");
        }

        public async Task PublishIfReadyAsync(Guid contestId)
        {
            try
            {
                // Check publish readiness
                PublishReadinessDTO check = await CheckPublishReadinessAsync(contestId);

                // If not ready, throw exception with details
                if (!check.IsReady)
                {
                    CoreException ex = new CoreException("PUBLISH_BLOCKED", "Contest is not ready to publish.", StatusCodes.Status409Conflict)
                    {
                        AdditionalData = new Dictionary<string, object> { ["missing"] = check.Missing }
                    };
                    throw ex;
                }

                // Get contest repository
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Fetch the contest
                Contest? contest = await contestRepo.GetByIdAsync(contestId);

                // Validate contest existence
                if (contest == null || contest.DeletedAt != null)
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

                // Get current time
                DateTime now = DateTime.UtcNow;

                // Fetch registration start from config
                string regStartKey = ConfigKeys.ContestRegStart(contestId);
                Config? regStartConfig = await configRepo.Entities
                    .Where(c => c.Key == regStartKey && c.DeletedAt == null)
                    .FirstOrDefaultAsync();

                // Fetch registration end from config
                string regEndKey = ConfigKeys.ContestRegEnd(contestId);
                Config? regEndConfig = await configRepo.Entities
                    .Where(c => c.Key == regEndKey && c.DeletedAt == null)
                    .FirstOrDefaultAsync();

                DateTime? registrationStart = ParseNullableUtc(regStartConfig?.Value);
                DateTime? registrationEnd = ParseNullableUtc(regEndConfig?.Value);

                // Determine contest status using helper method
                var (newStatus, shouldDeleteJobs) = await DetermineContestStatusAsync(
                    contest,
                    registrationStart,
                    registrationEnd,
                    now);

                // Delete scheduled jobs if needed
                if (shouldDeleteJobs)
                {
                    await DeleteContestScheduledJobsAsync(contestId);
                }

                // Update contest status only if not already Delayed
                contest.Status = newStatus;
                await contestRepo.UpdateAsync(contest);
                await _unitOfWork.SaveAsync();

                // Notify dashboard about status change
                await _dashboardNotifier.NotifyContestStatusChangedAsync();

                // Notify organizer dashboard
                string currentUserId = GetCurrentUserIdOrThrow();
                Guid organizerId = Guid.Parse(currentUserId);
                await _dashboardNotifier.NotifyOrganizerDashboardUpdatedAsync(organizerId);

                // Schedule state transitions only if not moving to Delayed status
                if (newStatus != ContestStatusEnum.Delayed.ToString())
                {
                    SafeEnqueue(() =>
                        BackgroundJob.Enqueue<ContestStateJob>(job => job.ScheduleContestStateTransitionsAsync(contestId)),
                        "ScheduleContestStateTransitionsAsync");
                }

                //  Log activity
                var actorId = GetCurrentUserGuidOrThrow();
                await SafeWriteActivityAsync(actorId, ActivityActions.ContestPublish, TargetTypes.Contest, contestId.ToString());

                // Notify participants if contest was delayed
                if (newStatus == ContestStatusEnum.Delayed.ToString())
                {
                    await SafeNotifyParticipantsAsync(contestId, NotificationTypes.ContestDelayed, new
                    {
                        contestId,
                        targetType = TargetTypes.Contest,
                        targetId = contestId.ToString(),
                        message = "Contest has been delayed due to insufficient team registrations."
                    });
                }
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                if (ex is CoreException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error publishing Contest: {ex.Message}");
            }
        }

        private async Task<(string NewStatus, bool ShouldDeleteJobs)> DetermineContestStatusAsync(
            Contest contest,
            DateTime? registrationStart,
            DateTime? registrationEnd,
            DateTime now)
        {
            string newStatus;
            bool shouldDeleteJobs = false;

            // Priority 1: Check if contest has ended
            if (contest.End.HasValue && now >= contest.End.Value)
            {
                newStatus = ContestStatusEnum.Completed.ToString();
            }
            // Priority 2: Check if contest is ongoing
            else if (contest.Start.HasValue && contest.End.HasValue && now >= contest.Start.Value && now < contest.End.Value)
            {
                newStatus = ContestStatusEnum.Ongoing.ToString();
            }
            // Priority 3: Check if registration has closed but contest hasn't started
            else if (registrationEnd.HasValue && now >= registrationEnd.Value
                && contest.Start.HasValue && now < contest.Start.Value)
            {
                // Check if contest has at least 1 team
                bool hasTeams = await CheckContestHasTeamsAsync(contest.ContestId);

                if (!hasTeams)
                {
                    // Set status to Delayed if no teams registered
                    newStatus = ContestStatusEnum.Delayed.ToString();
                    shouldDeleteJobs = true;
                }
                else if (contest.Status != ContestStatusEnum.RegistrationClosed.ToString())
                {
                    newStatus = ContestStatusEnum.RegistrationClosed.ToString();
                }
                else
                {
                    // Keep current status if already RegistrationClosed
                    newStatus = contest.Status;
                }
            }
            // Priority 4: Check if registration is open
            else if (registrationStart.HasValue && registrationEnd.HasValue
                && now >= registrationStart.Value && now < registrationEnd.Value)
            {
                newStatus = ContestStatusEnum.RegistrationOpen.ToString();
            }
            // Default: Published (before registration starts)
            else
            {
                newStatus = ContestStatusEnum.Published.ToString();
            }

            return (newStatus, shouldDeleteJobs);
        }

        private async Task<bool> CheckContestHasTeamsAsync(Guid contestId)
        {
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

            // Check if contest has at least 1 non-deleted team
            int teamCount = await teamRepo.Entities
                .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                .CountAsync();

            return teamCount > 0;
        }

        private async Task DeleteContestScheduledJobsAsync(Guid contestId)
        {
            try
            {
                // Get all rounds for the contest
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
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

                _logger.LogInformation("Deleted scheduled jobs for contest {ContestId} and {RoundCount} rounds",
                    contestId, roundIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete scheduled jobs for contest {ContestId}", contestId);
            }
        }

        public async Task<IReadOnlyList<ContestPolicyDTO>> GetContestPoliciesAsync(Guid contestId)
        {
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            Contest? contest = await contestRepo.Entities
                .FirstOrDefaultAsync(c => c.ContestId == contestId && c.DeletedAt == null);

            if (contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            string prefix = ConfigKeys.ContestPolicyPrefix(contestId);

            List<Config> configs = await configRepo.Entities
                .Where(c => c.Scope == "contest"
                            && c.DeletedAt == null
                            && c.Key.StartsWith(prefix))
                .ToListAsync();

            var result = configs.Select(c => new ContestPolicyDTO
            {
                Key = ExtractPolicyKeyFromKey(contestId, c.Key),
                Value = c.Value
            }).ToList();

            return result;
        }

        public async Task SetContestPoliciesAsync(Guid contestId, IList<ContestPolicyDTO> policies)
        {
            if (policies == null)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Policies cannot be null.");

            Contest contest = await GetContestOwnedByCurrentOrganizerAsync(contestId);

            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            foreach (var policy in policies)
            {
                if (string.IsNullOrWhiteSpace(policy.Key))
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Policy key is required.");

                string normalizedKey = policy.Key.Trim().ToLowerInvariant();
                string configKey = ConfigKeys.ContestPolicy(contest.ContestId, normalizedKey);
                string value = policy.Value?.Trim() ?? string.Empty;

                await UpsertConfigAsync(configRepo, configKey, value);
            }

            await _unitOfWork.SaveAsync();
        }

        public async Task DeleteContestPolicyAsync(Guid contestId, string policyKey)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                if (string.IsNullOrWhiteSpace(policyKey))
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Policy key is required.");

                Contest contest = await GetContestOwnedByCurrentOrganizerAsync(contestId);

                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                string normalizedKey = policyKey.Trim().ToLowerInvariant();
                string configKey = ConfigKeys.ContestPolicy(contest.ContestId, normalizedKey);

                // Delete associated teams and their members
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();

                // Get all teams associated with the contest
                List<Team> teams = await teamRepo.Entities
                    .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                    .Include(t => t.TeamMembers)
                    .ToListAsync();

                if (teams.Any())
                {
                    DateTime now = DateTime.UtcNow;

                    // Soft delete all team members
                    foreach (Team team in teams)
                    {
                        List<TeamMember> teamMembers = team.TeamMembers.ToList();

                        foreach (TeamMember member in teamMembers)
                        {
                            await teamMemberRepo.DeleteAsync(member);
                        }

                        // Soft delete the team
                        team.DeletedAt = now;
                        await teamRepo.UpdateAsync(team);
                    }
                }

                // Save all changes
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error deleting Contest policy: {ex.Message}");
            }
        }

        private async Task<Contest> GetContestOwnedByCurrentOrganizerAsync(Guid contestId)
        {
            string currentUserId = GetCurrentUserIdOrThrow();

            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            Contest? contest = await contestRepo.Entities
                .FirstOrDefaultAsync(c => c.ContestId == contestId && c.DeletedAt == null);

            if (contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

            if (!string.Equals(contest.CreatedBy, currentUserId, StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(StatusCodes.Status403Forbidden, "FORBIDDEN", "Only the organizer who created this contest can modify policies.");

            return contest;
        }

        private string GetCurrentUserIdOrThrow()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || !user.Identity?.IsAuthenticated == true)
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Sign in required.");

            var id = user.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);

            if (string.IsNullOrWhiteSpace(id))
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Invalid user context.");

            return id;
        }

        // parse key "contest:{contestId}:policy:{policyKey}" → policyKey
        private static string ExtractPolicyKeyFromKey(Guid contestId, string key)
        {
            string prefix = ConfigKeys.ContestPolicyPrefix(contestId);
            return key.StartsWith(prefix, StringComparison.Ordinal)
                ? key.Substring(prefix.Length)
                : key;
        }

        private static async Task UpsertConfigAsync(IGenericRepository<Config> repo, string key, string value)
        {
            // Check for existing config
            Config? existing = await repo.Entities.FirstOrDefaultAsync(c => c.Key == key);

            // Insert or update accordingly
            if (existing == null)
            {
                await repo.InsertAsync(new Config
                {
                    Key = key,
                    Value = value,
                    Scope = "contest",
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

        private static async Task<string> SuggestAlternateNameAsync(string baseName, int year, IGenericRepository<Contest> repo)
        {
            // Suggest names with numeric suffixes until an available one is found
            int suffix = 2;
            string candidate;
            do
            {
                candidate = $"{baseName} ({suffix})";
                suffix++;
            }
            while (await repo.Entities.AnyAsync(c => c.Year == year && c.Name == candidate && c.DeletedAt == null));

            return candidate;
        }

        private static async Task<int> GetGlobalIntOrDefaultAsync(IGenericRepository<Config> repo, string key, int @default)
        {
            // Fetch the config value
            string? value = await repo.Entities.Where(c => c.Key == key && c.DeletedAt == null)
                                           .Select(c => c.Value).FirstOrDefaultAsync();
            return int.TryParse(value, out int n) ? n : @default;
        }

        private static async Task<int> GetContestPolicyDaysAsync(
            Guid contestId,
            string policyKey,
            int defaultDays,
            IGenericRepository<Config> repo)
        {
            string key = ConfigKeys.ContestPolicy(contestId, policyKey);
            string? value = await repo.Entities.Where(c => c.Key == key && c.DeletedAt == null)
                                               .Select(c => c.Value).FirstOrDefaultAsync();
            return int.TryParse(value, out int n) && n >= 0 ? n : defaultDays;
        }

        private static async Task<int?> GetGlobalNullableIntAsync(IGenericRepository<Config> repo, string key)
        {
            // Fetch the config value
            string? value = await repo.Entities.Where(c => c.Key == key && c.DeletedAt == null)
                                           .Select(c => c.Value).FirstOrDefaultAsync();
            return int.TryParse(value, out int n) ? n : null;
        }

        public async Task CancelContestAsync(Guid contestId)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Get contest repository
                IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

                // Fetch the contest
                Contest? existingContest = await contestRepo
                    .Entities
                    .Where(c => c.ContestId == contestId && c.DeletedAt == null)
                    .FirstOrDefaultAsync();

                // Validate contest existence
                if (existingContest == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");
                }

                // Update contest status to Cancelled
                existingContest.Status = ContestStatusEnum.Cancelled.ToString();

                // Save to database
                await contestRepo.UpdateAsync(existingContest);
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();

                // Notify dashboard about status change
                await _dashboardNotifier.NotifyContestStatusChangedAsync();

                // Notify all mentors with teams in the contest
                await NotifiMentorDashboardContestUpdated(contestId);

                // Notify organizer dashboard
                string currentUserId = GetCurrentUserIdOrThrow();
                Guid organizerId = Guid.Parse(currentUserId);
                await _dashboardNotifier.NotifyOrganizerDashboardUpdatedAsync(organizerId);

                // Notify mentors dashboard
                await NotifiMentorDashboardContestUpdated(existingContest.ContestId);

                // Notify activity log
                var actorId = GetCurrentUserGuidOrThrow();
                await SafeWriteActivityAsync(actorId, ActivityActions.ContestCancel, TargetTypes.Contest, existingContest.ContestId.ToString());

                // Notify participants
                await SafeNotifyParticipantsAsync(existingContest.ContestId, NotificationTypes.ContestCancelled, new
                {
                    contestId = existingContest.ContestId,
                    targetType = TargetTypes.Contest,
                    targetId = existingContest.ContestId.ToString(),
                    message = "This contest has been cancelled."
                });

            }
            catch (Exception ex)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error cancelling Contest: {ex.Message}");
            }
        }

        public async Task<GetContestDTO> StartContestNowAsync(Guid contestId)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                if (contestId == Guid.Empty)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid contest ID.");

                Contest contest = await GetContestOwnedByCurrentOrganizerAsync(contestId);

                DateTime now = DateTime.UtcNow;

                // Prevent invalid timeline if End already passed
                if (contest.End.HasValue && contest.End.Value <= now)
                    throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Contest already ended. Cannot start now.");

                var configRepo = _unitOfWork.GetRepository<Config>();
                DateTime? regEnd = await GetNullableDateAsync(configRepo, ConfigKeys.ContestRegEnd(contestId));
                if (regEnd.HasValue && regEnd.Value > now)
                    throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Registration has not ended yet. Cannot start contest now.");

                contest.Start = now;

                // Update status
                contest.Status = ContestStatusEnum.Ongoing.ToString();

                await _unitOfWork.GetRepository<Contest>().UpdateAsync(contest);
                await _unitOfWork.SaveAsync();

                _unitOfWork.CommitTransaction();

                // Notify dashboard about status change
                await _dashboardNotifier.NotifyContestStatusChangedAsync();

                // Notify all mentors with teams in the contest
                await NotifiMentorDashboardContestUpdated(contestId);

                // Notify organizer dashboard
                Guid organizerId = Guid.Parse(contest.CreatedBy!);
                await _dashboardNotifier.NotifyOrganizerDashboardUpdatedAsync(organizerId);

                // Notify mentors
                if (ShouldNotifyParticipants(contest.Status))
                {
                    await NotifiMentorDashboardContestUpdated(contest.ContestId);
                }

                // Log activity
                var actorId = GetCurrentUserGuidOrThrow();
                await SafeWriteActivityAsync(actorId, ActivityActions.ContestStartNow, TargetTypes.Contest, contest.ContestId.ToString());

                // Notify participants
                await SafeNotifyParticipantsAsync(contest.ContestId, NotificationTypes.ContestStarted, new
                {
                    contestId = contest.ContestId,
                    name = contest.Name,
                    targetType = TargetTypes.Contest,
                    targetId = contest.ContestId.ToString(),
                    message = $"Contest '{contest.Name}' has started."
                });

                // Schedule state transitions
                SafeEnqueue(() =>
                    BackgroundJob.Enqueue<ContestStateJob>(job => job.ScheduleContestStateTransitionsAsync(contest.ContestId)),
                    "ScheduleContestStateTransitionsAsync");


                return await GetContestByIdAsync(contest.ContestId);
            }
            catch
            {
                _unitOfWork.RollBack();
                throw;
            }
        }

        public async Task<GetContestDTO> EndContestNowAsync(Guid contestId)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                if (contestId == Guid.Empty)
                    throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Invalid contest ID.");

                Contest contest = await GetContestOwnedByCurrentOrganizerAsync(contestId);

                DateTime now = DateTime.UtcNow;

                // Must not end before start
                if (contest.Start.HasValue && contest.Start.Value >= now)
                    throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Contest has not started yet (start time is in the future).");

                // Must not contest end < any existing round end
                var roundRepo = _unitOfWork.GetRepository<Round>();
                var violatingRounds = await roundRepo.Entities
                    .Where(r => r.ContestId == contestId && r.DeletedAt == null && r.End > now)
                    .Select(r => r.Name)
                    .ToListAsync();

                if (violatingRounds.Any())
                {
                    var ex = new CoreException("END_BLOCKED", "Cannot end contest now because some rounds end after now.", StatusCodes.Status409Conflict)
                    {
                        AdditionalData = new Dictionary<string, object>
                        {
                            ["rounds"] = violatingRounds
                        }
                    };
                    throw ex;
                }

                contest.End = now;
                contest.Status = ContestStatusEnum.Completed.ToString();

                await _unitOfWork.GetRepository<Contest>().UpdateAsync(contest);
                await _unitOfWork.SaveAsync();

                _unitOfWork.CommitTransaction();

                // Notify dashboard about status change
                await _dashboardNotifier.NotifyContestStatusChangedAsync();

                // Notify organizer dashboard
                Guid organizerId = Guid.Parse(contest.CreatedBy!);
                await _dashboardNotifier.NotifyOrganizerDashboardUpdatedAsync(organizerId);

                // Notify mentors dashboard
                await NotifiMentorDashboardContestUpdated(contest.ContestId);

                // Log activity
                var actorId = GetCurrentUserGuidOrThrow();
                await SafeWriteActivityAsync(actorId, ActivityActions.ContestEndNow, TargetTypes.Contest, contest.ContestId.ToString());

                // Notify participants
                await SafeNotifyParticipantsAsync(contest.ContestId, NotificationTypes.ContestEnded, new
                {
                    contestId = contest.ContestId,
                    name = contest.Name,
                    targetType = TargetTypes.Contest,
                    targetId = contest.ContestId.ToString(),
                    message = $"Contest '{contest.Name}' has ended."
                });

                // Schedule state transitions
                SafeEnqueue(() =>
                    BackgroundJob.Enqueue<ContestStateJob>(job => job.ScheduleContestStateTransitionsAsync(contest.ContestId)),
                    "ScheduleContestStateTransitionsAsync");

                return await GetContestByIdAsync(contest.ContestId);
            }
            catch
            {
                _unitOfWork.RollBack();
                throw;
            }
        }


        private static void ValidateTeamMemberRange(int min, int max)
        {
            if (min < 1)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    "TeamMembersMin must be >= 1.");

            if (max < 1)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    "TeamMembersMax must be >= 1.");

            if (max < min)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST,
                    "TeamMembersMax must be >= TeamMembersMin.");
        }

        // Get current user ID or throw if unauthenticated/invalid
        private Guid GetCurrentUserGuidOrThrow()
        {
            var idStr = GetCurrentUserIdOrThrow();
            if (!Guid.TryParse(idStr, out var id) || id == Guid.Empty)
                throw new ErrorException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Invalid user id.");
            return id;
        }

        //  Get user IDs of all participants (students and mentors) in the contest
        private async Task<List<Guid>> GetParticipantUserIdsAsync(Guid contestId)
        {
            var teamRepo = _unitOfWork.GetRepository<Team>();

            var studentIds = await teamRepo.Entities
                .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                .SelectMany(t => t.TeamMembers
                .Select(tm => tm.Student.UserId))
                .ToListAsync();

            var mentorIds = await teamRepo.Entities
                .Where(t => t.ContestId == contestId && t.DeletedAt == null && t.MentorId != Guid.Empty)
                .Select(t => t.Mentor.UserId)
                .ToListAsync();

            return studentIds.Concat(mentorIds)
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();
        }

        // Notify participants of an event
        private async Task TryNotifyParticipantsAsync(Guid contestId, string type, object payload)
        {
            var ids = await GetParticipantUserIdsAsync(contestId);
            if (ids.Count == 0) return;
            await _notificationService.CreateInAppToUsersAsync(ids, type, payload);
        }

        // Write activity log safely, logging any exceptions
        private async Task SafeWriteActivityAsync(Guid actorId, string action, string targetType, string targetId)
        {
            try
            {
                await _activityLogWriter.TryWriteAsync(actorId, action, targetType, targetId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Activity log failed. Action={Action}, Target={TargetType}, TargetId={TargetId}, ActorId={ActorId}",
                    action, targetType, targetId, actorId);
            }
        }

        // Notify participants safely, logging any exceptions
        private async Task SafeNotifyParticipantsAsync(Guid contestId, string type, object payload)
        {
            try
            {
                await TryNotifyParticipantsAsync(contestId, type, payload);
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

        public async Task<string> DownloadContestReportZipAsync(Guid contestId)
        {
            // Ensure the caller is the contest owner (organizer) and contest exists
            Contest contest = await GetContestOwnedByCurrentOrganizerAsync(contestId);

            // Config key storing the cached report attachment id
            string reportConfigKey = ConfigKeys.ContestReport(contestId);

            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
            IGenericRepository<Attachment> attachmentRepo = _unitOfWork.GetRepository<Attachment>();

            // Check for existing cached report attachment
            Config? reportConfig = await configRepo.Entities
                .Where(c => c.Key == reportConfigKey && c.DeletedAt == null)
                .FirstOrDefaultAsync();

            // If cache exists, verify attachment
            if (reportConfig != null
                && Guid.TryParse(reportConfig.Value, out Guid attachmentId)
                && attachmentId != Guid.Empty)
            {
                Attachment? existingAttachment = await attachmentRepo.Entities
                    .Where(a => a.AttachmentId == attachmentId && a.DeletedAt == null)
                    .FirstOrDefaultAsync();

                // Valid cached attachment found
                if (existingAttachment != null && !string.IsNullOrWhiteSpace(existingAttachment.Url))
                {
                    // return stored URL
                    return existingAttachment.Url;
                }

                // Log invalid cache scenario
                _logger.LogWarning(
                    "Contest report cache invalid. ContestId={ContestId}, AttachmentId={AttachmentId}",
                    contestId, attachmentId);
            }

            // build zip, upload, store attachment row, store config pointer
            _unitOfWork.BeginTransaction();

            try
            {
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                IGenericRepository<LeaderboardEntry> leaderboardRepo = _unitOfWork.GetRepository<LeaderboardEntry>();

                // Load contest rounds required for report
                List<Round> rounds = await roundRepo.Entities
                    .Where(r => r.ContestId == contestId && r.DeletedAt == null)
                    .Include(r => r.McqTest)
                    .Include(r => r.Problem)
                    .ToListAsync();

                // Load teams + related navigation properties required for report
                List<Team> teams = await teamRepo.Entities
                    .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                    .Include(t => t.School)
                    .Include(t => t.Mentor)
                        .ThenInclude(m => m.User)
                    .Include(t => t.TeamMembers)
                        .ThenInclude(tm => tm.Student)
                            .ThenInclude(s => s.User)
                    .ToListAsync();

                // Identify latest leaderboard snapshot time to export the latest leaderboard state
                DateTime? latestSnapshot = await leaderboardRepo.Entities
                    .Where(e => e.ContestId == contestId)
                    .MaxAsync(e => (DateTime?)e.SnapshotAt);

                // Load leaderboard entries for the latest snapshot
                List<LeaderboardEntry> leaderboardEntries = await leaderboardRepo.Entities
                    .Where(e => e.ContestId == contestId && (!latestSnapshot.HasValue || e.SnapshotAt == latestSnapshot.Value))
                    .ToListAsync();

                // Convert leaderboard entries to a TeamId, (Rank, Score) lookup
                Dictionary<Guid, (int? Rank, double? Score)> leaderboardByTeamId = leaderboardEntries
                    .GroupBy(e => e.TeamId)
                    .ToDictionary(g => g.Key, g =>
                    {
                        LeaderboardEntry entry = g.OrderByDescending(x => x.SnapshotAt).First();
                        return (entry.Rank, entry.Score);
                    });

                // Build rank map used for ordering output
                Dictionary<Guid, int> teamRank = teams.ToDictionary(
                    t => t.TeamId,
                    t => leaderboardByTeamId.TryGetValue(t.TeamId, out (int? Rank, double? Score) v) && v.Rank.HasValue
                        ? v.Rank.Value
                        : int.MaxValue);

                // Sort teams by rank then name for a stable report output
                List<Team> teamsSorted = teams
                    .OrderBy(t => teamRank[t.TeamId])
                    .ThenBy(t => t.Name)
                    .ToList();

                // Compute member latest scores per round
                Dictionary<(Guid TeamId, Guid RoundId, Guid StudentId), (double Score, string Status)> memberScores =
                    await GetLatestMemberRoundScoresAsync(contestId, teamsSorted);

                // Compute the team average score per round
                Dictionary<(Guid TeamId, Guid RoundId), double> teamRoundAvgScores =
                    GetTeamRoundAverageScores(rounds, teamsSorted, memberScores);

                // Build CSV contents
                string leaderboardCsv = BuildLeaderboardSummaryCsv(teamsSorted, leaderboardByTeamId, teamRank);
                string teamRoundCsv = BuildTeamRoundScoresCsv(rounds, teamsSorted, teamRoundAvgScores, teamRank);
                string memberRoundCsv = BuildMemberRoundScoresCsv(rounds, teamsSorted, memberScores, teamRank);

                // Create ZIP in-memory containing all CSV files
                byte[] zipBytes = CreateZip(new Dictionary<string, string>
                {
                    ["leaderboard_summary.csv"] = leaderboardCsv,
                    ["team_round_scores.csv"] = teamRoundCsv,
                    ["member_round_scores.csv"] = memberRoundCsv
                });

                // Prepare a friendly filename for Cloudinary
                string fileSafeContestName = ToSafeFileName(contest.Name);
                string zipFileName = $"contest-report-{contest.Year}-{fileSafeContestName}.zip";

                // Wrap bytes as an IFormFile
                IFormFile zipFormFile = CreateZipFormFile(zipBytes, zipFileName);

                // Upload to Cloudinary
                string url = await _cloudinaryService.UploadFileAsync(zipFormFile, CONTEST_REPORT_FOLDER);

                // Store uploaded file URL in attachments table.
                Attachment attachment = new Attachment
                {
                    AttachmentId = Guid.NewGuid(),
                    Url = url,
                    Type = CONTEST_REPORT_ATTACHEMENT_TYPE,
                    CreatedAt = DateTime.UtcNow,
                    DeletedAt = null
                };

                await attachmentRepo.InsertAsync(attachment);

                // Update config to point to new attachment
                await UpsertConfigAsync(configRepo, reportConfigKey, attachment.AttachmentId.ToString());

                await _unitOfWork.SaveAsync();
                _unitOfWork.CommitTransaction();

                return url;
            }
            catch
            {
                _unitOfWork.RollBack();
                throw;
            }
        }

        private static IFormFile CreateZipFormFile(byte[] content, string fileName)
        {
            // Convert in-memory bytes to IFormFile
            MemoryStream stream = new MemoryStream(content);
            FormFile formFile = new FormFile(stream, 0, stream.Length, name: "file", fileName: fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/zip"
            };

            return formFile;
        }

        private async Task<Dictionary<(Guid TeamId, Guid RoundId, Guid StudentId), (double Score, string Status)>> GetLatestMemberRoundScoresAsync(
            Guid contestId,
            List<Team> teams)
        {
            // Load latest submissions and MCQ attempts for all team members
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
            IGenericRepository<McqAttempt> attemptRepo = _unitOfWork.GetRepository<McqAttempt>();

            // Gather team IDs and student IDs for filtering
            List<Guid> teamIds = teams.Select(t => t.TeamId).ToList();
            HashSet<Guid> studentIds = teams.SelectMany(t => t.TeamMembers.Select(m => m.StudentId)).ToHashSet();

            // Define cancelled status strings to exclude cancelled entries
            string cancelledSubmission = SubmissionStatusEnum.Cancelled.ToString();
            string cancelledAttempt = McqAttemptStatusEnum.Cancelled.ToString();

            // Load all valid submissions for contest teams
            List<Submission> submissions = await submissionRepo.Entities
                .Where(s => teamIds.Contains(s.TeamId)
                            && studentIds.Contains(s.SubmittedByStudentId)
                            && s.DeletedAt == null
                            && (s.Status == null || s.Status != cancelledSubmission))
                .Include(s => s.Problem)
                .ToListAsync();

            // Get only the latest submission per (Team, Round, Student)
            List<Submission> latestSubmissions = submissions
                .Where(s => s.Problem != null)
                .GroupBy(s => new { s.TeamId, RoundId = s.Problem.RoundId, s.SubmittedByStudentId })
                .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
                .ToList();

            // Load all valid MCQ attempts for contest students
            List<McqAttempt> attempts = await attemptRepo.Entities
                .Where(a => studentIds.Contains(a.StudentId)
                            && a.DeletedAt == null
                            && (a.Status == null || a.Status != cancelledAttempt))
                .ToListAsync();

            // Get only the latest attempt per (Round, Student)
            List<McqAttempt> latestAttempts = attempts
                .GroupBy(a => new { a.RoundId, a.StudentId })
                .Select(g => g.OrderByDescending(x => x.End ?? x.Start).First())
                .ToList();

            // Build a lookup to find which teams a student belongs to
            Dictionary<Guid, List<Guid>> studentToTeams = teams
                .SelectMany(t => t.TeamMembers.Select(m => new { t.TeamId, m.StudentId }))
                .GroupBy(x => x.StudentId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.TeamId).Distinct().ToList());

            // Initialize result dictionary with score and status
            Dictionary<(Guid TeamId, Guid RoundId, Guid StudentId), (double Score, string Status)> result =
                new Dictionary<(Guid TeamId, Guid RoundId, Guid StudentId), (double Score, string Status)>();

            // Populate scores and statuses from submissions
            foreach (Submission s in latestSubmissions)
            {
                Guid roundId = s.Problem.RoundId;
                string status = s.Status ?? "Unknown";
                result[(s.TeamId, roundId, s.SubmittedByStudentId)] = (s.Score, status);
            }

            // Populate scores and statuses from MCQ attempts
            foreach (McqAttempt a in latestAttempts)
            {
                // Skip if student not found in team lookup
                if (!studentToTeams.TryGetValue(a.StudentId, out List<Guid>? memberTeamIds))
                {
                    continue;
                }

                // Use 0 if score is null
                double score = a.Score ?? 0;
                string status = a.Status ?? "Unknown";

                // Add score and status for each team the student belongs to
                foreach (Guid teamId in memberTeamIds)
                {
                    result[(teamId, a.RoundId, a.StudentId)] = (score, status);
                }
            }

            return result;
        }

        private static Dictionary<(Guid TeamId, Guid RoundId), double> GetTeamRoundAverageScores(
            List<Round> rounds,
            List<Team> teams,
            Dictionary<(Guid TeamId, Guid RoundId, Guid StudentId), (double Score, string Status)> memberScores)
        {
            Dictionary<(Guid TeamId, Guid RoundId), double> result = new Dictionary<(Guid TeamId, Guid RoundId), double>();

            // Calculate average for each team in each round
            foreach (Team team in teams)
            {
                // Get all member IDs for this team
                List<Guid> memberStudentIds = team.TeamMembers.Select(m => m.StudentId).ToList();
                int memberCount = memberStudentIds.Count;

                foreach (Round round in rounds)
                {
                    double sum = 0;
                    int finishedCount = 0;

                    // Sum up scores for all members in this round
                    foreach (Guid studentId in memberStudentIds)
                    {
                        if (memberScores.TryGetValue((team.TeamId, round.RoundId, studentId), out (double Score, string Status) scoreData))
                        {
                            // Only add to sum if status is Finished
                            if (string.Equals(scoreData.Status, SubmissionStatusEnum.Finished.ToString(), StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(scoreData.Status, McqAttemptStatusEnum.Finished.ToString(), StringComparison.OrdinalIgnoreCase))
                            {
                                sum += scoreData.Score;
                                finishedCount++;
                            }
                        }
                    }

                    // Calculate average
                    double avg = finishedCount > 0 ? sum / memberCount : 0;
                    result[(team.TeamId, round.RoundId)] = avg;
                }
            }

            return result;
        }

        private static string BuildLeaderboardSummaryCsv(
            List<Team> teamsSorted,
            Dictionary<Guid, (int? Rank, double? Score)> leaderboardByTeamId,
            Dictionary<Guid, int> teamRank)
        {
            StringBuilder sb = new StringBuilder();

            // CSV header row
            sb.Append("No.;Rank;Team Name;School Name;Mentor Name;Total Score").Append(CSV_NEW_LINE);

            // Sort teams by rank
            List<Team> sortedTeams = teamsSorted
                .OrderBy(t => teamRank.TryGetValue(t.TeamId, out int r) ? r : int.MaxValue)
                .ToList();

            int rowNumber = 1;

            // Build CSV rows
            foreach (Team team in sortedTeams)
            {
                // Get rank or empty if not ranked
                int rank = teamRank.TryGetValue(team.TeamId, out int r) ? r : int.MaxValue;
                string rankValue = rank == int.MaxValue ? string.Empty : rank.ToString(CultureInfo.InvariantCulture);

                // Get total score from leaderboard
                double totalScore = leaderboardByTeamId.TryGetValue(team.TeamId, out (int? Rank, double? Score) v) && v.Score.HasValue
                    ? v.Score.Value
                    : 0;

                // Get mentor name and school name
                string mentorName = team.Mentor?.User?.Fullname ?? string.Empty;
                string schoolName = team.School?.Name ?? string.Empty;

                // Build CSV row with semicolon delimiter
                sb.Append(rowNumber.ToString(CultureInfo.InvariantCulture)).Append(CSV_DELIMITER)
                  .Append(CsvField(rankValue, CSV_DELIMITER)).Append(CSV_DELIMITER)
                  .Append(CsvField(team.Name, CSV_DELIMITER)).Append(CSV_DELIMITER)
                  .Append(CsvField(schoolName, CSV_DELIMITER)).Append(CSV_DELIMITER)
                  .Append(CsvField(mentorName, CSV_DELIMITER)).Append(CSV_DELIMITER)
                  .Append(totalScore.ToString(CultureInfo.InvariantCulture))
                  .Append(CSV_NEW_LINE);

                rowNumber++;
            }

            return sb.ToString();
        }

        private static string BuildTeamRoundScoresCsv(
            List<Round> rounds,
            List<Team> teamsSorted,
            Dictionary<(Guid TeamId, Guid RoundId), double> teamRoundAvgScores,
            Dictionary<Guid, int> teamRank)
        {
            StringBuilder sb = new StringBuilder();

            // CSV header row
            sb.Append("No.;Team Name;Round Name;Round Type;Team Average Score").Append(CSV_NEW_LINE);

            // Sort rounds ascending by name, then descending by start
            List<Round> roundsSorted = rounds
                .OrderBy(r => r.Name)
                .ThenByDescending(r => r.Start)
                .ToList();

            // Create a list of all rows
            List<(string TeamName, string RoundName, string RoundType, double Avg)> rows =
                new List<(string, string, string, double)>();

            foreach (Round round in roundsSorted)
            {
                foreach (Team team in teamsSorted)
                {
                    // Determine round type and convert to readable format
                    string roundType = round.McqTest != null
                        ? "MCQ Test"
                        : (round.Problem?.Type == ProblemTypeEnum.AutoEvaluation.ToString()
                            ? "Auto Evaluation"
                            : round.Problem?.Type ?? string.Empty);

                    // Get team average score for this round
                    double avg = teamRoundAvgScores.TryGetValue((team.TeamId, round.RoundId), out double v) ? v : 0;

                    rows.Add((team.Name, round.Name, roundType, avg));
                }
            }

            // Add rows
            int rowNumber = 1;
            foreach (var row in rows)
            {
                sb.Append(rowNumber.ToString(CultureInfo.InvariantCulture)).Append(CSV_DELIMITER)
                  .Append(CsvField(row.TeamName)).Append(CSV_DELIMITER)
                  .Append(CsvField(row.RoundName)).Append(CSV_DELIMITER)
                  .Append(CsvField(row.RoundType)).Append(CSV_DELIMITER)
                  .Append(row.Avg.ToString(CultureInfo.InvariantCulture))
                  .Append(CSV_NEW_LINE);

                rowNumber++;
            }

            return sb.ToString();
        }

        private static string BuildMemberRoundScoresCsv(
            List<Round> rounds,
            List<Team> teamsSorted,
            Dictionary<(Guid TeamId, Guid RoundId, Guid StudentId), (double Score, string Status)> memberScores,
            Dictionary<Guid, int> teamRank)
        {
            StringBuilder sb = new StringBuilder();

            // CSV header row
            sb.Append("No.;Team Name;Student Name;Round Name;Round Type;Student Score;Submission Status").Append(CSV_NEW_LINE);

            // Sort rounds ascending by name, then ascending by start
            List<Round> roundsSorted = rounds
                .OrderBy(r => r.Name)
                .ThenBy(r => r.Start)
                .ToList();

            // Create a list of all rows
            List<(string TeamName, string StudentName, string RoundName, string RoundType, double Score, string Status)> rows =
                new List<(string, string, string, string, double, string)>();

            foreach (Team team in teamsSorted)
            {
                // Sort members by student name for consistent ordering
                List<TeamMember> membersSorted = team.TeamMembers
                    .OrderBy(m => m.Student.User.Fullname)
                    .ToList();

                foreach (TeamMember member in membersSorted)
                {
                    // Get student name
                    string studentName = member.Student?.User?.Fullname ?? string.Empty;

                    foreach (Round round in roundsSorted)
                    {
                        // Determine round type and convert to readable format
                        string roundType = round.McqTest != null
                            ? ROUND_TYPE_MCQ_TEST
                            : (round.Problem?.Type == ProblemTypeEnum.AutoEvaluation.ToString()
                                ? ROUND_TYPE_AUTO_EVALUATION
                                : round.Problem?.Type ?? string.Empty);

                        // Get student score and status for this round
                        double score = 0;
                        string status = "Not Submitted";

                        if (memberScores.TryGetValue((team.TeamId, round.RoundId, member.StudentId), out (double Score, string Status) scoreData))
                        {
                            score = scoreData.Score;
                            status = scoreData.Status;
                        }

                        rows.Add((team.Name, studentName, round.Name, roundType, score, status));
                    }
                }
            }

            // Sort ascending by round name, then ascending by team name
            var sortedRows = rows
                .OrderBy(r => r.RoundName)
                .ThenBy(r => r.TeamName)
                .ToList();

            // Add rows
            int rowNumber = 1;
            foreach (var row in sortedRows)
            {
                sb.Append(rowNumber.ToString(CultureInfo.InvariantCulture)).Append(CSV_DELIMITER)
                  .Append(CsvField(row.TeamName)).Append(CSV_DELIMITER)
                  .Append(CsvField(row.StudentName)).Append(CSV_DELIMITER)
                  .Append(CsvField(row.RoundName)).Append(CSV_DELIMITER)
                  .Append(CsvField(row.RoundType)).Append(CSV_DELIMITER)
                  .Append(row.Score.ToString(CultureInfo.InvariantCulture)).Append(CSV_DELIMITER)
                  .Append(CsvField(row.Status))
                  .Append(CSV_NEW_LINE);

                rowNumber++;
            }

            return sb.ToString();
        }

        private static byte[] CreateZip(Dictionary<string, string> entries)
        {
            using MemoryStream ms = new MemoryStream();

            // Create ZIP archive in memory
            using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (KeyValuePair<string, string> kv in entries)
                {
                    // Create entry with fast compression
                    ZipArchiveEntry entry = zip.CreateEntry(kv.Key, CompressionLevel.Fastest);

                    // Write UTF-8 text with BOM
                    using Stream entryStream = entry.Open();
                    using StreamWriter writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                    writer.Write(kv.Value);
                }
            }

            // Return ZIP as byte array
            return ms.ToArray();
        }

        private static string CsvField(string? value, char delimiter = ',')
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            // Check if the value contains special characters that require quoting
            bool needsQuoting = value.Contains(delimiter) || value.Contains('"') || value.Contains('\n') || value.Contains('\r');

            if (needsQuoting)
            {
                string escaped = value.Replace("\"", "\"\"");
                return "\"" + escaped + "\"";
            }

            return value;
        }

        private static string ToSafeFileName(string name)
        {
            // Return default if input is empty
            if (string.IsNullOrWhiteSpace(name))
            {
                return "contest";
            }

            // Get system-specific invalid filename characters
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(name.Length);

            // Replace each invalid character with underscore
            foreach (char c in name.Trim())
            {
                sb.Append(invalid.Contains(c) ? '_' : c);
            }

            return sb.ToString();
        }

        public async Task<string> DownloadMentorContestReportAsync(Guid contestId)
        {
            // Get current user
            string currentUserId = GetCurrentUserIdOrThrow();

            // Get mentor repository
            IGenericRepository<Mentor> mentorRepo = _unitOfWork.GetRepository<Mentor>();

            // Find the mentor ID for current user
            Guid? mentorId = await mentorRepo.Entities
                .Where(m => m.UserId.ToString() == currentUserId && m.DeletedAt == null)
                .Select(m => m.MentorId)
                .FirstOrDefaultAsync();

            if (!mentorId.HasValue)
            {
                throw new ErrorException(StatusCodes.Status403Forbidden, "FORBIDDEN", "You must be a mentor to download this report.");
            }

            // Verify contest exists
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();
            Contest? contest = await contestRepo.Entities
                .FirstOrDefaultAsync(c => c.ContestId == contestId && c.DeletedAt == null);

            if (contest == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");
            }

            // Config key storing the cached mentor report attachment id
            string mentorReportConfigKey = ConfigKeys.ContestMentorReport(contestId, mentorId.Value);

            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
            IGenericRepository<Attachment> attachmentRepo = _unitOfWork.GetRepository<Attachment>();

            // Check for existing cached mentor report attachment
            Config? mentorReportConfig = await configRepo.Entities
                .Where(c => c.Key == mentorReportConfigKey && c.DeletedAt == null)
                .FirstOrDefaultAsync();

            // If cache exists, verify attachment
            if (mentorReportConfig != null
                && Guid.TryParse(mentorReportConfig.Value, out Guid attachmentId)
                && attachmentId != Guid.Empty)
            {
                Attachment? existingAttachment = await attachmentRepo.Entities
                    .Where(a => a.AttachmentId == attachmentId && a.DeletedAt == null)
                    .FirstOrDefaultAsync();

                // Valid cached attachment found
                if (existingAttachment != null && !string.IsNullOrWhiteSpace(existingAttachment.Url))
                {
                    // return stored URL
                    return existingAttachment.Url;
                }

                // Log invalid cache scenario
                _logger.LogWarning(
                    "Mentor contest report cache invalid. ContestId={ContestId}, MentorId={MentorId}, AttachmentId={AttachmentId}",
                    contestId, mentorId.Value, attachmentId);
            }

            // Build report, upload, store attachment row, store config pointer
            _unitOfWork.BeginTransaction();

            try
            {
                // Get mentor's team in this contest
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                Team? mentorTeam = await teamRepo.Entities
                    .Where(t => t.ContestId == contestId && t.MentorId == mentorId.Value && t.DeletedAt == null)
                    .Include(t => t.School)
                    .Include(t => t.TeamMembers)
                        .ThenInclude(tm => tm.Student)
                            .ThenInclude(s => s.User)
                    .FirstOrDefaultAsync();

                if (mentorTeam == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "You don't have a team in this contest.");
                }

                // Load contest rounds
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                List<Round> rounds = await roundRepo.Entities
                    .Where(r => r.ContestId == contestId && r.DeletedAt == null)
                    .Include(r => r.McqTest)
                    .Include(r => r.Problem)
                    .ToListAsync();

                // Compute member scores for mentor's team
                Dictionary<(Guid TeamId, Guid RoundId, Guid StudentId), (double Score, string Status)> memberScores =
                    await GetLatestMemberRoundScoresAsync(contestId, new List<Team> { mentorTeam });

                // Build CSV content
                string csvContent = BuildMentorTeamReportCsv(rounds, mentorTeam, memberScores);

                // Prepare CSV filename
                string csvFileName = $"mentor-team-report-{contest.Year}-{ToSafeFileName(contest.Name)}-{ToSafeFileName(mentorTeam.Name)}.csv";

                // Create ZIP in-memory containing the CSV file
                byte[] zipBytes = CreateZip(new Dictionary<string, string>
                {
                    [csvFileName] = csvContent
                });

                // Prepare a friendly filename for Cloudinary
                string zipFileName = $"mentor-team-report-{contest.Year}-{ToSafeFileName(contest.Name)}-{ToSafeFileName(mentorTeam.Name)}.zip";

                // Wrap bytes as an IFormFile
                IFormFile zipFormFile = CreateZipFormFile(zipBytes, zipFileName);

                // Upload to Cloudinary
                string url = await _cloudinaryService.UploadFileAsync(zipFormFile, CONTEST_REPORT_FOLDER);

                // Store uploaded file URL in attachments table
                Attachment attachment = new Attachment
                {
                    AttachmentId = Guid.NewGuid(),
                    Url = url,
                    Type = CONTEST_REPORT_ATTACHEMENT_TYPE,
                    CreatedAt = DateTime.UtcNow,
                    DeletedAt = null
                };

                await attachmentRepo.InsertAsync(attachment);

                // Update config to point to new attachment
                await UpsertConfigAsync(configRepo, mentorReportConfigKey, attachment.AttachmentId.ToString());

                await _unitOfWork.SaveAsync();
                _unitOfWork.CommitTransaction();

                return url;
            }
            catch
            {
                _unitOfWork.RollBack();
                throw;
            }
        }

        private static DateTime? ParseNullableUtc(string? isoString)
        {
            if (string.IsNullOrWhiteSpace(isoString)) return null;

            return DateTime.TryParse(
                isoString,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime parsed)
                ? parsed.ToUniversalTime()
                : null;
        }

        private static async Task<DateTime?> GetNullableDateAsync(IGenericRepository<Config> configRepo, string key)
        {
            string? val = await configRepo.Entities
                .Where(c => c.Key == key && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            return ParseNullableUtc(val);
        }

        public async Task<ContestTimelineDTO> GetContestTimelineAsync(Guid contestId)
        {
            var contestRepo = _unitOfWork.GetRepository<Contest>();
            var roundRepo = _unitOfWork.GetRepository<Round>();
            var configRepo = _unitOfWork.GetRepository<Config>();

            Contest? contest = await contestRepo.Entities
                .FirstOrDefaultAsync(c => c.ContestId == contestId && c.DeletedAt == null);

            if (contest == null)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND,
                    "Contest not found.");
            }

            string regStartKey = ConfigKeys.ContestRegStart(contestId);
            string regEndKey = ConfigKeys.ContestRegEnd(contestId);

            string? regStartVal = await configRepo.Entities
                .Where(c => c.Key == regStartKey && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            string? regEndVal = await configRepo.Entities
                .Where(c => c.Key == regEndKey && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            DateTime? regStart = ParseNullableUtc(regStartVal);
            DateTime? regEnd = ParseNullableUtc(regEndVal);

            var rounds = await roundRepo.Entities
                .Where(r => r.ContestId == contestId && r.DeletedAt == null)
                .OrderBy(r => r.Start)
                .ThenBy(r => r.End)
                .ToListAsync();

            var roundTimelines = new List<RoundTimelineDTO>();
            foreach (var r in rounds)
            {
                var timeline = await _roundService.GetRoundTimelineAsync(r.RoundId);
                roundTimelines.Add(timeline);
            }

            return new ContestTimelineDTO
            {
                ContestId = contestId,
                RegistrationStart = regStart,
                RegistrationEnd = regEnd,
                ContestStart = contest.Start?.ToUniversalTime(),
                ContestEnd = contest.End?.ToUniversalTime(),
                Rounds = roundTimelines
                    .OrderBy(t => t.Start)
                    .ThenBy(t => t.End)
                .ToList()
            };
        }

        public async Task SetRegistrationStartNowAsync(Guid contestId)
        {
            var configRepo = _unitOfWork.GetRepository<Config>();
            var contestRepo = _unitOfWork.GetRepository<Contest>();
            DateTime now = DateTime.UtcNow;

            Contest? contest = await contestRepo.Entities
                .FirstOrDefaultAsync(c => c.ContestId == contestId && c.DeletedAt == null);
            if (contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

            DateTime? regEnd = await GetNullableDateAsync(configRepo, ConfigKeys.ContestRegEnd(contestId));

            if (regEnd.HasValue && regEnd.Value <= now)
                throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Registration end time is earlier than or equal to requested start.");
            if (contest.Start.HasValue && contest.Start.Value <= now)
                throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Contest start time is earlier than or equal to requested registration start.");

            await UpsertConfigAsync(configRepo, ConfigKeys.ContestRegStart(contestId), now.ToString("o"));

            contest.Status = ContestStatusEnum.RegistrationOpen.ToString();
            await contestRepo.UpdateAsync(contest);
            await _unitOfWork.SaveAsync();

            // Notify dashboard
            await _dashboardNotifier.NotifyContestStatusChangedAsync();

            // Notify organizer dashboard
            string currentUserId = GetCurrentUserIdOrThrow();
            Guid organizerId = Guid.Parse(currentUserId);
            await _dashboardNotifier.NotifyOrganizerDashboardUpdatedAsync(organizerId);
        }

        public async Task SetRegistrationEndNowAsync(Guid contestId)
        {
            var configRepo = _unitOfWork.GetRepository<Config>();
            var contestRepo = _unitOfWork.GetRepository<Contest>();
            DateTime now = DateTime.UtcNow;

            Contest? contest = await contestRepo.Entities
                .FirstOrDefaultAsync(c => c.ContestId == contestId && c.DeletedAt == null);
            if (contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, ResponseCodeConstants.NOT_FOUND, "Contest not found.");

            DateTime? regStart = await GetNullableDateAsync(configRepo, ConfigKeys.ContestRegStart(contestId));

            if (regStart.HasValue && regStart.Value > now)
                throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Registration end cannot be before registration start.");
            if (contest.Start.HasValue && now > contest.Start.Value)
                throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Registration end cannot be after contest start.");

            await UpsertConfigAsync(configRepo, ConfigKeys.ContestRegEnd(contestId), now.ToString("o"));
            contest.Status = ContestStatusEnum.RegistrationClosed.ToString();
            await contestRepo.UpdateAsync(contest);

            // Cancel pending invitations
            await CancelPendingInvitationsForContestAsync(contestId);

            await _unitOfWork.SaveAsync();

            // Notify dashboard
            await _dashboardNotifier.NotifyContestStatusChangedAsync();

            // Notify mentors of teams in the contest
            IGenericRepository<Team> _teamRepo = _unitOfWork.GetRepository<Team>();

            // Notify all mentors with teams in the contest
            await NotifiMentorDashboardContestUpdated(contestId);

            // Notify organizer dashboard
            string currentUserId = GetCurrentUserIdOrThrow();
            Guid organizerId = Guid.Parse(currentUserId);
            await _dashboardNotifier.NotifyOrganizerDashboardUpdatedAsync(organizerId);

            await NotifiMentorDashboardContestUpdated(contestId);
        }

        private async Task NotifiMentorDashboardContestUpdated(Guid contestId)
        {
            // Notify all mentors with teams in the contest
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

            // Get distinct mentor IDs from teams in the contest
            List<Guid> mentorIds = teamRepo.Entities
                .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                .Select(t => t.MentorId)
                .Distinct()
                .ToList();

            foreach (Guid mentorId in mentorIds)
            {
                if (mentorId == Guid.Empty)
                    continue;

                // Notify mentor dashboard about contest update
                await _dashboardNotifier.NotifyMentorDashboardUpdatedAsync(mentorId);
            }
        }

        private static string BuildMentorTeamReportCsv(
            List<Round> rounds,
            Team team,
            Dictionary<(Guid TeamId, Guid RoundId, Guid StudentId), (double Score, string Status)> memberScores)
        {
            StringBuilder sb = new StringBuilder();

            // CSV header row with Status column
            sb.Append("No.;Team Name;Student Name;Round Name;Round Type;Student Score;Submission Status").Append(CSV_NEW_LINE);

            // Sort rounds ascending by name, then ascending by start
            List<Round> roundsSorted = rounds
                .OrderBy(r => r.Name)
                .ThenBy(r => r.Start)
                .ToList();

            // Create a list of all rows
            List<(string TeamName, string StudentName, string RoundName, string RoundType, double Score, string Status)> rows =
                new List<(string, string, string, string, double, string)>();

            // Sort members by student name
            List<TeamMember> membersSorted = team.TeamMembers
                .OrderBy(m => m.Student.User.Fullname)
                .ToList();

            foreach (TeamMember member in membersSorted)
            {
                // Get student name
                string studentName = member.Student?.User?.Fullname ?? string.Empty;

                foreach (Round round in roundsSorted)
                {
                    // Determine round type and convert to readable format
                    string roundType = round.McqTest != null
                        ? ROUND_TYPE_MCQ_TEST
                        : (round.Problem?.Type == ProblemTypeEnum.AutoEvaluation.ToString()
                            ? ROUND_TYPE_AUTO_EVALUATION
                            : round.Problem?.Type ?? string.Empty);

                    // Get student score and status for this round
                    double score = 0;
                    string status = "Not Submitted";

                    if (memberScores.TryGetValue((team.TeamId, round.RoundId, member.StudentId), out (double Score, string Status) scoreData))
                    {
                        score = scoreData.Score;
                        status = scoreData.Status;
                    }

                    rows.Add((team.Name, studentName, round.Name, roundType, score, status));
                }
            }

            // Sort ascending by round name, then ascending by student name
            var sortedRows = rows
                .OrderBy(r => r.RoundName)
                .ThenBy(r => r.StudentName)
                .ToList();

            // Add rows with No. column and Status column
            int rowNumber = 1;
            foreach (var row in sortedRows)
            {
                sb.Append(rowNumber.ToString(CultureInfo.InvariantCulture)).Append(CSV_DELIMITER)
                  .Append(CsvField(row.TeamName)).Append(CSV_DELIMITER)
                  .Append(CsvField(row.StudentName)).Append(CSV_DELIMITER)
                  .Append(CsvField(row.RoundName)).Append(CSV_DELIMITER)
                  .Append(CsvField(row.RoundType)).Append(CSV_DELIMITER)
                  .Append(row.Score.ToString(CultureInfo.InvariantCulture)).Append(CSV_DELIMITER)
                  .Append(CsvField(row.Status))
                  .Append(CSV_NEW_LINE);

                rowNumber++;
            }

            return sb.ToString();
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
        /// Validates year search parameter
        /// </summary>
        private void ValidateYearSearch(int? yearSearch)
        {
            if (yearSearch.HasValue && yearSearch < MIN_YEAR)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    $"Year must be greater than 1900");
            }
        }

        /// <summary>
        /// Validates date range parameters
        /// </summary>
        private void ValidateDateRange(DateTime? startDate, DateTime? endDate)
        {
            if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Start date cannot be later than end date.");
            }
        }

        /// <summary>
        /// Builds base contest query with includes
        /// </summary>
        private IQueryable<Contest> BuildBaseContestQuery(IGenericRepository<Contest> contestRepo)
        {
            return contestRepo
                .Entities
                .Where(c => !c.DeletedAt.HasValue)
                .Include(c => c.Rounds.Where(r => !r.DeletedAt.HasValue))
                    .ThenInclude(r => r.Problem)
                .Include(c => c.Rounds.Where(r => !r.DeletedAt.HasValue))
                    .ThenInclude(r => r.McqTest);
        }

        /// <summary>
        /// Applies participant filter based on user role
        /// </summary>
        private async Task<IQueryable<Contest>> ApplyParticipantFilterAsync(
            IQueryable<Contest> query,
            string? userRole,
            string? userId)
        {
            if (string.IsNullOrEmpty(userId))
                return query;

            if (userRole == RoleConstants.Student)
            {
                return await ApplyStudentFilterAsync(query, userId);
            }

            if (userRole == RoleConstants.Mentor)
            {
                return await ApplyMentorFilterAsync(query, userId);
            }

            if (userRole == RoleConstants.Judge)
            {
                return await ApplyJudgeFilterAsync(query, userId);
            }

            return query;
        }

        /// <summary>
        /// Applies student-specific contest filter
        /// </summary>
        private async Task<IQueryable<Contest>> ApplyStudentFilterAsync(
            IQueryable<Contest> query,
            string userId)
        {
            IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();

            Guid? studentId = await studentRepo.Entities
                .Where(s => s.UserId.ToString() == userId && s.DeletedAt == null)
                .Select(s => s.StudentId)
                .FirstOrDefaultAsync();

            if (studentId.HasValue)
            {
                query = query.Include(c => c.Teams)
                             .ThenInclude(t => t.TeamMembers);

                query = query.Where(c => c.Teams.Any(t =>
                    t.TeamMembers.Any(tm => tm.StudentId == studentId.Value)
                    && t.DeletedAt == null));
            }

            return query;
        }

        /// <summary>
        /// Applies mentor-specific contest filter
        /// </summary>
        private async Task<IQueryable<Contest>> ApplyMentorFilterAsync(
            IQueryable<Contest> query,
            string userId)
        {
            IGenericRepository<Mentor> mentorRepo = _unitOfWork.GetRepository<Mentor>();

            Guid? mentorId = await mentorRepo.Entities
                .Where(m => m.UserId.ToString() == userId && m.DeletedAt == null)
                .Select(m => m.MentorId)
                .FirstOrDefaultAsync();

            if (mentorId.HasValue)
            {
                query = query.Include(c => c.Teams);

                query = query.Where(c => c.Teams.Any(t =>
                    t.MentorId == mentorId.Value
                 && t.DeletedAt == null));
            }

            return query;
        }

        /// <summary>
        /// Applies judge-specific contest filter
        /// </summary>
        private async Task<IQueryable<Contest>> ApplyJudgeFilterAsync(
            IQueryable<Contest> query,
            string userId)
        {
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            // Get all contest IDs where this user is a judge
            string judgeKeyPrefix = $"contest:";
            string judgeKeySuffix = $":judge:{userId}";

            List<string> judgeConfigs = await configRepo.Entities
                .Where(c => c.Key.StartsWith(judgeKeyPrefix)
                         && c.Key.EndsWith(judgeKeySuffix)
                         && c.DeletedAt == null)
                .Select(c => c.Key)
                .ToListAsync();

            // Extract contest IDs from config keys
            List<Guid> contestIds = judgeConfigs
                .Select(key => {
                    // Extract contest ID from key format
                    string[] parts = key.Split(':');
                    if (parts.Length >= 2 && Guid.TryParse(parts[1], out Guid contestId))
                    {
                        return (Guid?)contestId;
                    }
                    return null;
                })
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            // Filter contests to only those where the user is a judge
            if (contestIds.Any())
            {
                query = query.Where(c => contestIds.Contains(c.ContestId));
            }

            return query;
        }

        /// <summary>
        /// Maps a contest entity to GetContestDTO with all related data
        /// </summary>
        private GetContestDTO MapContestEntityToDTO(
            Contest contest,
            ILookup<string, Config> configLookup,
            Dictionary<Guid, string> organizerNames,
            Dictionary<string, Config> timeLimitDict,
            Dictionary<string, Config> mockTestWeightDict)
        {
            GetContestDTO contestDTO = _mapper.Map<GetContestDTO>(contest);

            contestDTO.rounds = MapContestRounds(contest, timeLimitDict, mockTestWeightDict);
            contestDTO.CreatedById = Guid.Parse(contest.CreatedBy!);
            contestDTO.CreatedByName = organizerNames.GetValueOrDefault(
                contestDTO.CreatedById,
                UNKNOWN_ORGANIZER);

            MapContestTimeProperties(contestDTO, contest);
            MapContestConfigurationProperties(contestDTO, contest.ContestId, configLookup);

            return contestDTO;
        }

        /// <summary>
        /// Maps contest rounds to DTOs
        /// </summary>
        private List<GetRoundDTO> MapContestRounds(
            Contest contest,
            Dictionary<string, Config> timeLimitDict,
            Dictionary<string, Config> mockTestWeightDict)
        {
            return contest.Rounds
                .Where(r => !r.DeletedAt.HasValue)
                .Select(r => MapRoundDTO(r, contest.Name, timeLimitDict, mockTestWeightDict))
                .OrderBy(r => r.Start)
                .ToList();
        }

        /// <summary>
        /// Maps a single round to GetRoundDTO
        /// </summary>
        private GetRoundDTO MapRoundDTO(
            Round round,
            string contestName,
            Dictionary<string, Config> timeLimitDict,
            Dictionary<string, Config> mockTestWeightDict)
        {
            GetRoundDTO roundDTO = _mapper.Map<GetRoundDTO>(round);

            roundDTO.RoundName = round.Name;
            roundDTO.ContestName = contestName;
            roundDTO.Start = round.Start;
            roundDTO.End = round.End;

            ApplyTimeLimitConfig(roundDTO, round.RoundId, timeLimitDict);
            MapRoundProblemOrTest(roundDTO, round);
            ApplyMockTestWeightConfig(roundDTO, round, mockTestWeightDict);

            return roundDTO;
        }

        /// <summary>
        /// Applies time limit configuration to round DTO
        /// </summary>
        private void ApplyTimeLimitConfig(
            GetRoundDTO roundDTO,
            Guid roundId,
            Dictionary<string, Config> timeLimitDict)
        {
            string timeLimitKey = ConfigKeys.RoundTimeLimitSeconds(roundId);
            if (timeLimitDict.TryGetValue(timeLimitKey, out Config? timeLimitConfig)
                && int.TryParse(timeLimitConfig.Value, out int timeLimit))
            {
                roundDTO.TimeLimitSeconds = timeLimit;
            }
        }

        /// <summary>
        /// Applies mock test weight configuration to round DTO for auto evaluation rounds with mock tests
        /// </summary>
        private void ApplyMockTestWeightConfig(
            GetRoundDTO roundDTO,
            Round round,
            Dictionary<string, Config> mockTestWeightDict)
        {
            // Check if this is an auto evaluation round with mock test
            if (round.Problem != null
                && round.Problem.Type == PROBLEM_TYPE_AUTO_EVALUATION
                && round.Problem.TestType == AUTO_MOCK_TEST_TEST_TYPE)
            {
                string weightKey = ConfigKeys.RoundWeight(round.RoundId);
                if (mockTestWeightDict.TryGetValue(weightKey, out Config? weightConfig)
                    && double.TryParse(weightConfig.Value, out double weight))
                {
                    roundDTO.Problem!.MockTestWeight = weight;
                }
            }
        }

        /// <summary>
        /// Maps problem or MCQ test to round DTO
        /// </summary>
        private void MapRoundProblemOrTest(GetRoundDTO roundDTO, Round round)
        {
            if (round.Problem != null)
            {
                roundDTO.ProblemType = round.Problem.Type;
                roundDTO.Problem = _mapper.Map<GetProblemDTO>(round.Problem);
            }
            else if (round.McqTest != null)
            {
                roundDTO.ProblemType = PROBLEM_TYPE_MCQ_TEST;
                roundDTO.McqTest = _mapper.Map<GetMcqTestDTO>(round.McqTest);
            }
        }

        /// <summary>
        /// Maps contest time properties to DTO
        /// </summary>
        private void MapContestTimeProperties(GetContestDTO contestDTO, Contest contest)
        {
            contestDTO.Start = contest.Start;
            contestDTO.End = contest.End;
            contestDTO.CreatedAt = contest.CreatedAt;
        }

        /// <summary>
        /// Maps contest configuration properties to DTO
        /// </summary>
        private void MapContestConfigurationProperties(
            GetContestDTO contestDTO,
            Guid contestId,
            ILookup<string, Config> configLookup)
        {
            // Fetch team members max from config
            string teamMemberMaxKey = ConfigKeys.ContestTeamMembersMax(contestId);
            Config? teamMemberMaxConfig = configLookup[teamMemberMaxKey].FirstOrDefault();
            if (teamMemberMaxConfig != null && int.TryParse(teamMemberMaxConfig.Value, out int teamMemberMax))
            {
                contestDTO.TeamMembersMax = teamMemberMax;
            }

            // Fetch team members min from config
            string teamMemberMinKey = ConfigKeys.ContestTeamMembersMin(contestId);
            Config? teamMemberMinConfig = configLookup[teamMemberMinKey].FirstOrDefault();
            if (teamMemberMinConfig != null && int.TryParse(teamMemberMinConfig.Value, out int teamMemberMin))
            {
                contestDTO.TeamMembersMin = teamMemberMin;
            }

            // Fetch team limit max from config
            string teamLimitMaxKey = ConfigKeys.ContestTeamLimitMax(contestId);
            Config? teamLimitMaxConfig = configLookup[teamLimitMaxKey].FirstOrDefault();
            if (teamLimitMaxConfig != null && int.TryParse(teamLimitMaxConfig.Value, out int teamLimitMax))
            {
                contestDTO.TeamLimitMax = teamLimitMax;
            }

            // Fetch registration start from config
            string regStartKey = ConfigKeys.ContestRegStart(contestId);
            Config? regStartConfig = configLookup[regStartKey].FirstOrDefault();
            if (regStartConfig != null && DateTime.TryParse(regStartConfig.Value, out DateTime regStart))
            {
                contestDTO.RegistrationStart = regStart;
            }

            // Fetch registration end from config
            string regEndKey = ConfigKeys.ContestRegEnd(contestId);
            Config? regEndConfig = configLookup[regEndKey].FirstOrDefault();
            if (regEndConfig != null && DateTime.TryParse(regEndConfig.Value, out DateTime regEnd))
            {
                contestDTO.RegistrationEnd = regEnd;
            }

            // Fetch rewards text from config
            string rewardsKey = ConfigKeys.ContestRewards(contestId);
            Config? rewardsConfig = configLookup[rewardsKey].FirstOrDefault();
            if (rewardsConfig != null && !string.IsNullOrWhiteSpace(rewardsConfig.Value))
            {
                contestDTO.RewardsText = rewardsConfig.Value;
            }

            contestDTO.AppealSubmitDays = GetPolicyDaysFromLookup(
                configLookup, contestId, ContestPolicyKeys.AppealSubmitDays, DEFAULT_APPEAL_SUBMIT_DAYS);
            contestDTO.AppealReviewDays = GetPolicyDaysFromLookup(
                configLookup, contestId, ContestPolicyKeys.AppealReviewDays, DEFAULT_APPEAL_REVIEW_DAYS);
            contestDTO.JudgeRescoreDays = GetPolicyDaysFromLookup(
                configLookup, contestId, ContestPolicyKeys.JudgeRescoreDays, DEFAULT_JUDGE_RESCORE_DAYS);
        }

        private int GetPolicyDaysFromLookup(
            ILookup<string, Config> configLookup,
            Guid contestId,
            string policyKey,
            int defaultDays)
        {
            string key = ConfigKeys.ContestPolicy(contestId, policyKey);
            Config? config = configLookup[key].FirstOrDefault();
            if (config != null && int.TryParse(config.Value, out int parsed) && parsed >= 0)
            {
                return parsed;
            }
            return defaultDays;
        }

        /// <summary>
        /// Validates all contest DTO properties
        /// </summary>
        private void ValidateContestDTO(
            string? name,
            int year,
            DateTime? start,
            DateTime? end,
            DateTime? regStart,
            DateTime? regEnd,
            int? teamMembersMin,
            int? teamMembersMax,
            int? teamLimitMax)
        {
            ValidateContestName(name);
            ValidateContestYear(year);
            ValidateContestDates(start, end, regStart, regEnd);
            ValidateTeamConfiguration(teamMembersMin, teamMembersMax, teamLimitMax);
        }

        private void ValidatePolicyDays(params int?[] days)
        {
            foreach (int? day in days)
            {
                if (day.HasValue && day.Value < 0)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Policy day values must be non-negative.");
                }
            }
        }

        /// <summary>
        /// Validates contest name
        /// </summary>
        private void ValidateContestName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Contest name is required.");
            }
        }

        /// <summary>
        /// Validates contest year
        /// </summary>
        private void ValidateContestYear(int year)
        {
            if (year < DateTime.UtcNow.Year)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Contest year cannot be in the past.");
            }
        }

        /// <summary>
        /// Validates contest and registration dates
        /// </summary>
        private void ValidateContestDates(
            DateTime? start,
            DateTime? end,
            DateTime? regStart,
            DateTime? regEnd)
        {
            if (start.HasValue && end.HasValue && start.Value >= end.Value)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Contest start date must be earlier than end date.");
            }

            if (regStart.HasValue && regEnd.HasValue && regStart.Value >= regEnd.Value)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Registration start date must be earlier than registration end date.");
            }

            if (regStart.HasValue && start.HasValue && regStart.Value >= start.Value)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Registration start date must be earlier than contest start date.");
            }

            if (regEnd.HasValue && start.HasValue && regEnd.Value >= start.Value)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Registration end date must be earlier than contest start date.");
            }
        }

        /// <summary>
        /// Validates team configuration parameters
        /// </summary>
        private void ValidateTeamConfiguration(
            int? teamMembersMin,
            int? teamMembersMax,
            int? teamLimitMax)
        {
            if (teamMembersMin.HasValue && teamMembersMin.Value < 1)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Team members minimum must be at least 1.");
            }

            if (teamMembersMax.HasValue && teamMembersMax.Value < 1)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Team members maximum must be at least 1.");
            }

            if (teamMembersMin.HasValue && teamMembersMax.HasValue &&
                teamMembersMin.Value > teamMembersMax.Value)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Team members minimum cannot be greater than team members maximum.");
            }

            if (teamLimitMax.HasValue && teamLimitMax.Value < 1)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Team limit maximum must be at least 1.");
            }
        }

        /// <summary>
        /// Applies all search filters to the query
        /// </summary>
        private IQueryable<Contest> ApplySearchFilters(
            IQueryable<Contest> query,
            Guid? idSearch,
            Guid? creatorIdSearch,
            Guid? roundIdSearch,
            string? nameSearch,
            int? yearSearch,
            DateTime? startDate,
            DateTime? endDate)
        {
            // Apply ID filter
            if (idSearch.HasValue)
            {
                query = query.Where(c => c.ContestId == idSearch.Value);
            }

            // Apply creator ID filter
            if (creatorIdSearch.HasValue)
            {
                query = query.Where(c => Guid.Parse(c.CreatedBy!) == creatorIdSearch.Value);
            }

            // Apply round ID filter
            if (roundIdSearch.HasValue)
            {
                query = query.Where(c => c.Rounds.Any(r => r.RoundId == roundIdSearch));
            }

            // Apply name search filter
            if (!string.IsNullOrWhiteSpace(nameSearch))
            {
                query = query.Where(c => c.Name.Contains(nameSearch));
            }

            // Apply year filter
            if (yearSearch.HasValue)
            {
                query = query.Where(c => c.Year == yearSearch.Value);
            }

            // Apply start date filter
            if (startDate.HasValue)
            {
                query = query.Where(c => c.Start >= startDate.Value);
            }

            // Apply end date filter
            if (endDate.HasValue)
            {
                query = query.Where(c => c.End <= endDate.Value);
            }

            return query;
        }

        /// <summary>
        /// Loads all related data efficiently (configs, organizers, time limits)
        /// </summary>
        private async Task<(ILookup<string, Config> configLookup, Dictionary<Guid, string> organizerNames, Dictionary<string, Config> timeLimitDict, Dictionary<string, Config> mockTestWeightDict)>
            LoadRelatedDataAsync(IReadOnlyCollection<Contest> contests)
        {
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
            IGenericRepository<User> userRepo = _unitOfWork.GetRepository<User>();

            // Extract contest IDs
            List<Guid> contestIds = contests.Select(c => c.ContestId).ToList();

            // Load all configs for all contests in one query
            List<Config> configs = await configRepo.Entities
                .Where(c => contestIds.Any(id => c.Key.Contains(id.ToString())) && c.DeletedAt == null)
                .ToListAsync();

            ILookup<string, Config> configLookup = configs.ToLookup(c => c.Key);

            // Load organizer names
            List<Guid> organizerGuids = contests
                .Select(c => c.CreatedBy)
                .Where(id => Guid.TryParse(id, out _))
                .Select(id => Guid.Parse(id!))
                .Distinct()
                .ToList();

            Dictionary<Guid, string> organizerNames = await userRepo.Entities
                .Where(u => organizerGuids.Contains(u.UserId) && !u.DeletedAt.HasValue)
                .ToDictionaryAsync(u => u.UserId, u => u.Fullname);

            // Load time limit configs
            List<Guid> roundIds = contests
                .SelectMany(c => c.Rounds)
                .Select(r => r.RoundId)
                .Distinct()
                .ToList();

            List<Config> timeLimitConfigs = await configRepo.Entities
                .Where(c => roundIds.Any(rid => c.Key.Contains(rid.ToString()))
                            && c.Key.Contains(TIME_LIMIT_SECONDS_KEY_SUFFIX)
                            && c.DeletedAt == null)
                .ToListAsync();

            Dictionary<string, Config> timeLimitDict = timeLimitConfigs.ToDictionary(c => c.Key);

            // Load mock test weight configs
            List<Config> mockTestWeightConfigs = await configRepo.Entities
                .Where(c => roundIds.Any(rid => c.Key.Contains(rid.ToString()))
                            && c.Key.Contains(MOCK_TEST_WEIGHT_KEY_SUFFIX)
                            && c.DeletedAt == null)
                .ToListAsync();

            Dictionary<string, Config> mockTestWeightDict = mockTestWeightConfigs.ToDictionary(c => c.Key);

            return (configLookup, organizerNames, timeLimitDict, mockTestWeightDict);
        }

        /// <summary>
        /// Fetches contest with all necessary includes
        /// </summary>
        private async Task<Contest> FetchContestWithIncludesAsync(IGenericRepository<Contest> contestRepo, Guid id)
        {
             Contest? contest = await contestRepo.Entities
                .Where(c => c.ContestId == id && !c.DeletedAt.HasValue)
                .Include(c => c.Rounds.Where(r => !r.DeletedAt.HasValue))
                    .ThenInclude(r => r.Problem)
                .Include(c => c.Rounds.Where(r => !r.DeletedAt.HasValue))
                    .ThenInclude(r => r.McqTest)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();

            // Validate contest existence and accessibility
            ValidateContest(contest);

            // Sort rounds by start date ascending
            if (contest != null && contest.Rounds != null && contest.Rounds.Any())
            {
                contest.Rounds = contest.Rounds.OrderBy(r => r.Start).ToList();
            }

            return contest!;
        }

        /// <summary>
        /// Loads all related data for a single contest
        /// </summary>
        private async Task<(ILookup<string, Config> configLookup, string organizerName, Dictionary<string, Config> timeLimitDict, Dictionary<string, Config> mockTestWeightDict)>
            LoadContestRelatedDataAsync(Contest contest)
        {
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
            IGenericRepository<User> userRepo = _unitOfWork.GetRepository<User>();

            // Load configs for this contest
            List<Config> configs = await configRepo.Entities
                .Where(c => c.Key.Contains(contest.ContestId.ToString()) && c.DeletedAt == null)
                .ToListAsync();

            ILookup<string, Config> configLookup = configs.ToLookup(c => c.Key);

            // Get organizer name
            Guid organizerGuid = Guid.TryParse(contest.CreatedBy, out Guid guid) ? guid : Guid.Empty;
            string organizerName = await userRepo.Entities
                .Where(u => organizerGuid == u.UserId && !u.DeletedAt.HasValue)
                .Select(u => u.Fullname)
                .FirstOrDefaultAsync() ?? UNKNOWN_ORGANIZER;

            // Load time limits
            List<Guid> roundIds = contest.Rounds.Select(r => r.RoundId).Distinct().ToList();
            List<Config> timeLimitConfigs = await configRepo.Entities
                .Where(c => roundIds.Any(rid => c.Key.Contains(rid.ToString()))
                            && c.Key.Contains(TIME_LIMIT_SECONDS_KEY_SUFFIX)
                            && c.DeletedAt == null)
                .ToListAsync();

            Dictionary<string, Config> timeLimitDict = timeLimitConfigs.ToDictionary(c => c.Key);

            // Load weights for mock test rounds
            List<Config> mockTestWeightConfigs = await configRepo.Entities
                .Where(c => roundIds.Any(rid => c.Key.Contains(rid.ToString()))
                            && c.Key.Contains(MOCK_TEST_WEIGHT_KEY_SUFFIX)
                            && c.DeletedAt == null)
                .ToListAsync();

            Dictionary<string, Config> mockTestWeightDict = mockTestWeightConfigs.ToDictionary(c => c.Key);

            return (configLookup, organizerName, timeLimitDict, mockTestWeightDict);
        }

        /// <summary>
        /// Maps a single contest to DTO
        /// </summary>
        private GetContestDTO MapSingleContestToDTO(
            Contest contest,
            ILookup<string, Config> configLookup,
            string organizerName,
            Dictionary<string, Config> timeLimitDict,
            Dictionary<string, Config> mockTestWeightDict)
        {
            GetContestDTO contestDTO = _mapper.Map<GetContestDTO>(contest);

            // Map rounds
            contestDTO.rounds = MapContestRounds(contest, timeLimitDict, mockTestWeightDict);

            // Map creator info
            contestDTO.CreatedById = Guid.Parse(contest.CreatedBy!);
            contestDTO.CreatedByName = organizerName;

            // Map time properties
            MapContestTimeProperties(contestDTO, contest);

            // Map configuration properties
            MapContestConfigurationProperties(contestDTO, contest.ContestId, configLookup);

            return contestDTO;
        }

        /// <summary>
        /// Validates update contest input parameters
        /// </summary>
        private void ValidateUpdateContestInput(Guid id, UpdateContestDTO contestDTO)
        {
            if (contestDTO == null)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST, "Contest data cannot be null.");
            }

            if (id == Guid.Empty)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST, "Invalid contest ID.");
            }

            // Validate all contest properties
            ValidateContestDTO(
                contestDTO.Name,
                contestDTO.Year,
                contestDTO.Start,
                contestDTO.End,
                contestDTO.RegistrationStart,
                contestDTO.RegistrationEnd,
                contestDTO.TeamMembersMin,
                contestDTO.TeamMembersMax,
                contestDTO.TeamLimitMax);

            ValidatePolicyDays(
                contestDTO.AppealSubmitDays,
                contestDTO.AppealReviewDays,
                contestDTO.JudgeRescoreDays);
        }

        /// <summary>
        /// Gets existing contest or throws if not found
        /// </summary>
        private async Task<Contest> GetExistingContestOrThrowAsync(IGenericRepository<Contest> contestRepo, Guid id)
        {
            Contest? existingContest = await contestRepo.GetByIdAsync(id);

            ValidateContest(existingContest);

            return existingContest!;
        }

        /// <summary>
        /// Checks for duplicate contest name in the same year
        /// </summary>
        private async Task CheckDuplicateContestNameAsync(
            IGenericRepository<Contest> contestRepo,
            string name,
            int year,
            string currentName)
        {
            string nameTrim = name.Trim();

            bool exists = await contestRepo.Entities
                .AnyAsync(c => c.Year == year && c.Name == nameTrim && c.Name != currentName && c.DeletedAt == null);

            if (exists)
            {
                string? suggestion = await SuggestAlternateNameAsync(nameTrim, year, contestRepo);
                CoreException ex = new CoreException(ResponseCodeConstants.DUPLICATE,
                    "Contest name already exists for this year.", StatusCodes.Status409Conflict)
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        ["suggestion"] = suggestion
                    }
                };
                throw ex;
            }
        }

        /// <summary>
        /// Captures old contest values for notification purposes
        /// </summary>
        private (DateTime? Start, DateTime? End, string Name, string Status) CaptureOldContestValues(Contest contest)
        {
            return (contest.Start, contest.End, contest.Name, contest.Status);
        }

        /// <summary>
        /// Updates contest entity with DTO values
        /// </summary>
        private async Task UpdateContestEntityAsync(
            Contest existingContest,
            UpdateContestDTO contestDTO,
            IGenericRepository<Config> configRepo)
        {
            ValidateModifyContest(existingContest);

            // Update basic properties
            _mapper.Map(contestDTO, existingContest);

            if (contestDTO.Start.HasValue)
                existingContest.Start = contestDTO.Start.Value;
            if (contestDTO.End.HasValue)
                existingContest.End = contestDTO.End.Value;

            // Handle image upload
            if (contestDTO.ImageFile != null)
            {
                existingContest.ImgUrl = await UploadContestImageAsync(contestDTO.ImageFile);
            }

            // Update configurations
            await UpdateContestConfigurationsAsync(existingContest.ContestId, contestDTO, configRepo);
        }

        /// <summary>
        /// Uploads contest image and returns URL
        /// </summary>
        private async Task<string> UploadContestImageAsync(IFormFile imageFile)
        {
            if (!CloudinaryHelpers.IsImageFile(imageFile))
            {
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Uploaded file is not a valid image. Required .jpeg, .jpg, .png file");
            }

            return await _cloudinaryService.UploadFileAsync(imageFile, CONTEST_IMAGE_FOLDER);
        }

        /// <summary>
        /// Updates contest configurations (team settings, registration, rewards)
        /// </summary>
        private async Task UpdateContestConfigurationsAsync(
            Guid contestId,
            UpdateContestDTO contestDTO,
            IGenericRepository<Config> configRepo)
        {
            // Get team member settings
            int teamMembersMin = contestDTO.TeamMembersMin
                                 ?? await GetGlobalIntOrDefaultAsync(configRepo, ConfigKeys.Defaults_TeamMembersMin, 1);
            int teamMembersMax = contestDTO.TeamMembersMax
                                 ?? await GetGlobalIntOrDefaultAsync(configRepo, ConfigKeys.Defaults_TeamMembersMax, 4);

            ValidateTeamMemberRange(teamMembersMin, teamMembersMax);

            int? teamLimitMax = contestDTO.TeamLimitMax
                                 ?? await GetGlobalNullableIntAsync(configRepo, ConfigKeys.Defaults_TeamLimitMax);

            // Update config entries
            await UpsertConfigAsync(configRepo, ConfigKeys.ContestTeamMembersMin(contestId), teamMembersMin.ToString());
            await UpsertConfigAsync(configRepo, ConfigKeys.ContestTeamMembersMax(contestId), teamMembersMax.ToString());

            if (teamLimitMax.HasValue)
                await UpsertConfigAsync(configRepo, ConfigKeys.ContestTeamLimitMax(contestId), teamLimitMax.Value.ToString());

            // Set registration times
            if (contestDTO.RegistrationStart.HasValue)
            {
                await UpsertConfigAsync(configRepo, ConfigKeys.ContestRegStart(contestId),
                    DateTimeHelpers.ToIso8601String(contestDTO.RegistrationStart.Value));
            }

            if (contestDTO.RegistrationEnd.HasValue)
            {
                await UpsertConfigAsync(configRepo, ConfigKeys.ContestRegEnd(contestId),
                    DateTimeHelpers.ToIso8601String(contestDTO.RegistrationEnd.Value));
            }

            // Set rewards text
            if (!string.IsNullOrWhiteSpace(contestDTO.RewardsText))
                await UpsertConfigAsync(configRepo, ConfigKeys.ContestRewards(contestId), contestDTO.RewardsText!.Trim());

            // Appeal / judge policy (keep existing if not provided)
            int submitDays = contestDTO.AppealSubmitDays
                             ?? await GetContestPolicyDaysAsync(contestId, ContestPolicyKeys.AppealSubmitDays, DEFAULT_APPEAL_SUBMIT_DAYS, configRepo);
            int reviewDays = contestDTO.AppealReviewDays
                             ?? await GetContestPolicyDaysAsync(contestId, ContestPolicyKeys.AppealReviewDays, DEFAULT_APPEAL_REVIEW_DAYS, configRepo);
            int rescoreDays = contestDTO.JudgeRescoreDays
                             ?? await GetContestPolicyDaysAsync(contestId, ContestPolicyKeys.JudgeRescoreDays, DEFAULT_JUDGE_RESCORE_DAYS, configRepo);

            await UpsertConfigAsync(configRepo, ConfigKeys.ContestPolicy(contestId, ContestPolicyKeys.AppealSubmitDays), submitDays.ToString());
            await UpsertConfigAsync(configRepo, ConfigKeys.ContestPolicy(contestId, ContestPolicyKeys.AppealReviewDays), reviewDays.ToString());
            await UpsertConfigAsync(configRepo, ConfigKeys.ContestPolicy(contestId, ContestPolicyKeys.JudgeRescoreDays), rescoreDays.ToString());
        }

        /// <summary>
        /// Performs post-update operations (logging, notifications, scheduling)
        /// </summary>
        private async Task PerformPostUpdateOperationsAsync(
            Contest contest,
            (DateTime? Start, DateTime? End, string Name, string Status) oldValues)
        {
            // Log activity
            var actorId = GetCurrentUserGuidOrThrow();
            await SafeWriteActivityAsync(actorId, ActivityActions.ContestUpdate, TargetTypes.Contest, contest.ContestId.ToString());

            // Notify participants if contest is in relevant status
            if (ShouldNotifyParticipants(contest.Status))
            {
                await SafeNotifyParticipantsAsync(contest.ContestId, NotificationTypes.ContestUpdated, new
                {
                    contestId = contest.ContestId,
                    name = contest.Name,
                    oldName = oldValues.Name,
                    oldStart = oldValues.Start,
                    oldEnd = oldValues.End,
                    newStart = contest.Start,
                    newEnd = contest.End,
                    targetType = TargetTypes.Contest,
                    targetId = contest.ContestId.ToString(),
                    message = $"Contest '{contest.Name}' has been updated."
                });
            }

            // Schedule state transitions
            SafeEnqueue(() =>
                BackgroundJob.Enqueue<ContestStateJob>(job => job.ScheduleContestStateTransitionsAsync(contest.ContestId)),
                "ScheduleContestStateTransitionsAsync");
        }

        /// <summary>
        /// Determines if participants should be notified based on contest status
        /// </summary>
        private bool ShouldNotifyParticipants(string status)
        {
            return status == ContestStatusEnum.Published.ToString()
                || status == ContestStatusEnum.RegistrationOpen.ToString()
                || status == ContestStatusEnum.RegistrationClosed.ToString()
                || status == ContestStatusEnum.Ongoing.ToString()
                || status == ContestStatusEnum.Cancelled.ToString();
        }

        /// <summary>
        /// Validates create contest input
        /// </summary>
        private void ValidateCreateContestInput(CreateContestAdvancedDTO dto)
        {
            if (dto == null)
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST, "Payload cannot be null.");

            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST, "Name is required.");

            int currentYear = DateTime.UtcNow.Year;
            if (dto.Year < currentYear)
                throw new ErrorException(StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST, $"Year must be ≥ {currentYear}.");

            ValidateContestDTO(
                dto.Name,
                dto.Year,
                dto.Start,
                dto.End,
                dto.RegistrationStart,
                dto.RegistrationEnd,
                dto.TeamMembersMin,
                dto.TeamMembersMax,
                dto.TeamLimitMax);

            ValidatePolicyDays(
                dto.AppealSubmitDays,
                dto.AppealReviewDays,
                dto.JudgeRescoreDays);
        }

        /// <summary>
        /// Checks for duplicate contest name for new contest
        /// </summary>
        private async Task CheckDuplicateContestNameForNewAsync(
            IGenericRepository<Contest> contestRepo,
            string name,
            int year)
        {
            bool exists = await contestRepo.Entities
                .AnyAsync(c => c.Year == year && c.Name == name && c.DeletedAt == null);

            if (exists)
            {
                string? suggestion = await SuggestAlternateNameAsync(name, year, contestRepo);
                CoreException ex = new CoreException(ResponseCodeConstants.DUPLICATE,
                    "Contest name already exists for this year.", StatusCodes.Status409Conflict)
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        ["suggestion"] = suggestion
                    }
                };
                throw ex;
            }
        }

        /// <summary>
        /// Uploads image if provided, returns empty string otherwise
        /// </summary>
        private async Task<string> UploadImageIfProvidedAsync(IFormFile? imageFile)
        {
            if (imageFile == null)
                return string.Empty;

            return await UploadContestImageAsync(imageFile);
        }

        /// <summary>
        /// Creates contest entity from DTO
        /// </summary>
        private Contest CreateContestEntity(
            CreateContestAdvancedDTO dto,
            string currentUserId,
            string nameTrim,
            string imageUrl)
        {
            Contest entity = _mapper.Map<Contest>(dto);
            entity.ContestId = Guid.NewGuid();
            entity.Name = nameTrim;
            entity.Status = ContestStatusEnum.Draft.ToString();
            entity.CreatedAt = DateTime.UtcNow;
            entity.CreatedBy = currentUserId;
            entity.ImgUrl = imageUrl;

            if (dto.Start.HasValue)
                entity.Start = dto.Start.Value;
            if (dto.End.HasValue)
                entity.End = dto.End.Value;

            return entity;
        }

        /// <summary>
        /// Configures new contest settings and returns configuration values
        /// </summary>
        private async Task<(int TeamMembersMin, int TeamMembersMax, int? TeamLimitMax)> ConfigureNewContestAsync(
            Guid contestId,
            CreateContestAdvancedDTO dto,
            IGenericRepository<Config> configRepo)
        {
            // Get team settings
            int teamMembersMin = dto.TeamMembersMin
                                 ?? await GetGlobalIntOrDefaultAsync(configRepo, ConfigKeys.Defaults_TeamMembersMin, 1);
            int teamMembersMax = dto.TeamMembersMax
                                 ?? await GetGlobalIntOrDefaultAsync(configRepo, ConfigKeys.Defaults_TeamMembersMax, 4);
            int? teamLimitMax = dto.TeamLimitMax
                                 ?? await GetGlobalNullableIntAsync(configRepo, ConfigKeys.Defaults_TeamLimitMax);

            // Insert config entries
            await UpsertConfigAsync(configRepo, ConfigKeys.ContestTeamMembersMin(contestId), teamMembersMin.ToString());
            await UpsertConfigAsync(configRepo, ConfigKeys.ContestTeamMembersMax(contestId), teamMembersMax.ToString());

            if (teamLimitMax.HasValue)
                await UpsertConfigAsync(configRepo, ConfigKeys.ContestTeamLimitMax(contestId), teamLimitMax.Value.ToString());

            // Set registration times
            if (dto.RegistrationStart.HasValue)
            {
                await UpsertConfigAsync(configRepo, ConfigKeys.ContestRegStart(contestId),
                    DateTimeHelpers.ToIso8601String(dto.RegistrationStart.Value));
            }

            if (dto.RegistrationEnd.HasValue)
            {
                await UpsertConfigAsync(configRepo, ConfigKeys.ContestRegEnd(contestId),
                    DateTimeHelpers.ToIso8601String(dto.RegistrationEnd.Value));
            }

            // Set rewards text
            if (!string.IsNullOrWhiteSpace(dto.RewardsText))
                await UpsertConfigAsync(configRepo, ConfigKeys.ContestRewards(contestId), dto.RewardsText!.Trim());

            return (teamMembersMin, teamMembersMax, teamLimitMax);
        }

        private async Task<(int AppealSubmitDays, int AppealReviewDays, int JudgeRescoreDays)> ConfigureContestPoliciesAsync(
            Guid contestId,
            int? appealSubmitDays,
            int? appealReviewDays,
            int? judgeRescoreDays,
            IGenericRepository<Config> configRepo)
        {
            int submitDays = appealSubmitDays ?? DEFAULT_APPEAL_SUBMIT_DAYS;
            int reviewDays = appealReviewDays ?? DEFAULT_APPEAL_REVIEW_DAYS;
            int rescoreDays = judgeRescoreDays ?? DEFAULT_JUDGE_RESCORE_DAYS;

            await UpsertConfigAsync(configRepo, ConfigKeys.ContestPolicy(contestId, ContestPolicyKeys.AppealSubmitDays), submitDays.ToString());
            await UpsertConfigAsync(configRepo, ConfigKeys.ContestPolicy(contestId, ContestPolicyKeys.AppealReviewDays), reviewDays.ToString());
            await UpsertConfigAsync(configRepo, ConfigKeys.ContestPolicy(contestId, ContestPolicyKeys.JudgeRescoreDays), rescoreDays.ToString());

            return (submitDays, reviewDays, rescoreDays);
        }

        /// <summary>
        /// Performs post-create operations
        /// </summary>
        private async Task PerformPostCreateOperationsAsync(Contest entity)
        {
            // Log activity
            var actorId = GetCurrentUserGuidOrThrow();
            await SafeWriteActivityAsync(actorId, ActivityActions.ContestCreate, TargetTypes.Contest, entity.ContestId.ToString());

            // Schedule state transitions
            SafeEnqueue(() =>
                BackgroundJob.Enqueue<ContestStateJob>(job => job.ScheduleContestStateTransitionsAsync(entity.ContestId)),
                "ScheduleContestStateTransitionsAsync");
        }

        /// <summary>
        /// Maps contest entity to ContestCreatedDTO
        /// </summary>
        private ContestCreatedDTO MapToContestCreatedDTO(
            Contest entity,
            CreateContestAdvancedDTO dto,
            string imageUrl,
            (int TeamMembersMin, int TeamMembersMax, int? TeamLimitMax) configValues,
            (int AppealSubmitDays, int AppealReviewDays, int JudgeRescoreDays) policyValues)
        {
            ContestCreatedDTO created = _mapper.Map<ContestCreatedDTO>(entity);
            created.TeamMembersMin = configValues.TeamMembersMin;
            created.TeamMembersMax = configValues.TeamMembersMax;
            created.TeamLimitMax = configValues.TeamLimitMax;
            created.RewardsText = dto.RewardsText;
            created.RegistrationStart = dto.RegistrationStart;
            created.RegistrationEnd = dto.RegistrationEnd;
            created.Start = entity.Start;
            created.End = entity.End;
            created.CreatedAt = entity.CreatedAt;
            created.imageUrl = imageUrl;
            created.AppealSubmitDays = policyValues.AppealSubmitDays;
            created.AppealReviewDays = policyValues.AppealReviewDays;
            created.JudgeRescoreDays = policyValues.JudgeRescoreDays;

            return created;
        }

        /// <summary>
        /// Make pending team invitations expired when contest registration time ends
        /// </summary>
        private async Task CancelPendingInvitationsForContestAsync(Guid contestId)
        {
            try
            {
                IGenericRepository<TeamInvite> inviteRepo = _unitOfWork.GetRepository<TeamInvite>();
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

                // Get all team IDs for the contest
                List<Guid> contestTeamIds = await teamRepo.Entities
                    .Where(t => t.ContestId == contestId && t.DeletedAt == null)
                    .Select(t => t.TeamId)
                    .ToListAsync();

                if (!contestTeamIds.Any())
                {
                    _logger.LogDebug("No teams found for contest {ContestId}, skipping invitation cancellation.", contestId);
                    return;
                }

                List<TeamInvite> pendingInvitations = await inviteRepo.Entities
                    .Where(inv => contestTeamIds.Contains(inv.TeamId)
                               && inv.Status == TeamInviteStatusConstants.Pending)
                    .ToListAsync();

                if (pendingInvitations.Any())
                {
                    foreach (TeamInvite invitation in pendingInvitations)
                    {
                        invitation.Status = TeamInviteStatusConstants.Expired;
                    }

                    _logger.LogInformation(
                        "Expired {Count} pending invitation(s) for contest {ContestId}",
                        pendingInvitations.Count, contestId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel pending invitations for contest {ContestId}", contestId);
            }
        }

        /// <summary>
        /// Validates access to the contest based on user role
        /// </summary>
        private void ValidateContest(Contest? contest)
        {
            // Get logged-in user role
            string? userRole = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role);

            // Validate contest exists
            if (contest == null || contest.DeletedAt.HasValue)
            {
                throw new ErrorException(StatusCodes.Status404NotFound,
                    ResponseCodeConstants.NOT_FOUND, "Contest not found.");
            }

            // Restrict access to contests of other organizers
            if (userRole == RoleConstants.ContestOrganizer)
            {
                // Get current user ID
                string userId = GetCurrentUserIdOrThrow();

                if (contest.CreatedBy!.ToLower() != userId)
                {
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        ResponseCodeConstants.FORBIDDEN,
                        "Access denied to this contest.");
                }
            }
        }

        private void ValidateModifyContest(Contest? contest)
        {
            // Restrict modifications to contest with Draft or Delayed status
            if (contest!.Status != ContestStatusEnum.Draft.ToString() &&
                contest.Status != ContestStatusEnum.Delayed.ToString())
            {
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    "Modifications are only allowed for contest with Draft or Delayed status.");
            }
        }
    }
}
