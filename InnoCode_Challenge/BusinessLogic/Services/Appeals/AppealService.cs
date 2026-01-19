using AutoMapper;
using BusinessLogic.Helpers;
using BusinessLogic.IServices.Appeals;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.Dashboards;
using BusinessLogic.IServices.FileStorages;
using BusinessLogic.IServices.NotificationsAndLogs;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.AppealDTOs;
using Repository.DTOs.AppealEvidenceDTOs;
using Repository.IRepositories;
using System.Security.Claims;
using Utility.Constant;
using Utility.Enums;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Appeals
{
    public class AppealService : IAppealService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly ILeaderboardEntryService _leaderboardEntryService;
        private readonly INotificationService _notificationService;
        private readonly IActivityLogWriter _logWriter;
        private readonly IDashboardNotifierService _dashboardNotifier;

        private const string APPEAL_EVIDENCE_FOLDER = "appeal_evidences";
        private const int DEFAULT_APPEAL_SUBMIT_DAYS = 2;
        private const int DEFAULT_APPEAL_REVIEW_DAYS = 1;
        private const int DEFAULT_JUDGE_RESCORE_DAYS = 1;

        // Constructor
        public AppealService(
            IMapper mapper,
            IUOW uow,
            IHttpContextAccessor httpContextAccessor,
            ICloudinaryService cloudinaryService,
            ILeaderboardEntryService leaderboardEntryService,
            INotificationService notificationService,
            IActivityLogWriter logWriter,
            IDashboardNotifierService dashboardNotifier)
        {
            _mapper = mapper;
            _unitOfWork = uow;
            _httpContextAccessor = httpContextAccessor;
            _cloudinaryService = cloudinaryService;
            _leaderboardEntryService = leaderboardEntryService;
            _notificationService = notificationService;
            _logWriter = logWriter;
            _dashboardNotifier = dashboardNotifier;
        }

        public async Task<GetAppealDTO> CreateAppealAsync(CreateAppealDTO dto)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                // Get current user
                string currentUserId = GetCurrentUserIdOrThrow();

                // Validate user is a mentor
                string? userRole = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role);
                if (userRole != RoleConstants.Mentor)
                {
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        ResponseCodeConstants.FORBIDDEN,
                        "Only mentors can create appeals.");
                }

                // Get repositories
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                IGenericRepository<Mentor> mentorRepo = _unitOfWork.GetRepository<Mentor>();
                IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
                IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();
                IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
                IGenericRepository<AppealEvidence> evidenceRepo = _unitOfWork.GetRepository<AppealEvidence>();

                // Validate round exists
                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == dto.RoundId && r.DeletedAt == null)
                    .Include(r => r.McqTest)
                    .Include(r => r.Problem)
                    .Include(r => r.Contest)
                    .FirstOrDefaultAsync();

                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Round not found.");
                }

                if (round.IsRetakeRound)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Appeals are not allowed for retake rounds.");
                }

                DateTime now = DateTime.UtcNow;
                DateTime submitDeadline = await GetAppealSubmitDeadlineUtcAsync(round, configRepo);
                if (now > submitDeadline)
                {
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        "APPEAL_SUBMIT_DEADLINE_PASSED",
                        "Appeal submission deadline has passed.");
                }

                string contestStatus = round.Contest.Status ?? string.Empty;

                // Check if contest is ongoing
                if (contestStatus != ContestStatusEnum.Ongoing.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Appeals can only be created for rounds in ongoing contests.");
                }

                // Validate team exists
                Team? team = await teamRepo.Entities
                    .FirstOrDefaultAsync(t => t.TeamId == dto.TeamId && t.DeletedAt == null);

                if (team == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Team not found.");
                }

                // Verify mentor owns this team
                Mentor? mentor = await mentorRepo.Entities
                    .FirstOrDefaultAsync(m => m.UserId.ToString() == currentUserId && m.DeletedAt == null);

                if (mentor == null || team.MentorId != mentor.MentorId)
                {
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        ResponseCodeConstants.FORBIDDEN,
                        "You can only create appeals for your own teams.");
                }

                // Validate student exists
                Student? student = await studentRepo.Entities
                    .FirstOrDefaultAsync(s => s.StudentId == dto.StudentId && s.DeletedAt == null);

                if (student == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Student not found.");
                }

                // Verify student is a member of the team
                bool isStudentInTeam = await teamMemberRepo.Entities
                    .AnyAsync(tm => tm.TeamId == dto.TeamId && tm.StudentId == dto.StudentId);

                if (!isStudentInTeam)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Student is not a member of the specified team.");
                }

                // Validate Retake resolution has a corresponding retake round
                if (dto.AppealResolution == AppealResolutionEnum.Retake)
                {
                    bool hasRetakeRound = await roundRepo.Entities
                        .AnyAsync(r => r.MainRoundId == dto.RoundId
                                      && r.IsRetakeRound
                                      && r.DeletedAt == null);

                    if (!hasRetakeRound)
                    {
                        throw new ErrorException(StatusCodes.Status400BadRequest,
                            ResponseCodeConstants.BADREQUEST,
                            "Cannot create appeal with Retake resolution. This round does not have a retake round configured.");
                    }
                }

                // Determine target type based on round content
                string targetType;
                if (round.McqTest != null)
                {
                    targetType = AppealTargetTypeEnum.McqTest.ToString();
                }
                else if (round.Problem != null)
                {
                    string problemType = round.Problem.Type ?? string.Empty;
                    targetType = problemType.Equals("AutoEvaluation")
                        ? AppealTargetTypeEnum.AutoEvaluation.ToString()
                        : AppealTargetTypeEnum.Manual.ToString();
                }
                else
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Round has no associated problem or MCQ test.");
                }

                // Check for existing opened appeal for this specific student
                Guid targetId = dto.RoundId;
                bool existingAppeal = await appealRepo.Entities
                    .AnyAsync(a => a.TargetType == targetType
                                && a.TargetId == targetId
                                && a.OwnerId == student.UserId
                                && a.State == AppealStateEnum.Opened.ToString()
                                && a.DeletedAt == null);

                if (existingAppeal)
                {
                    throw new ErrorException(StatusCodes.Status409Conflict,
                        ResponseCodeConstants.DUPLICATE,
                        "A pending appeal already exists for this student in this round.");
                }

                // Get current mentor ID
                string currentMentorId = await mentorRepo.Entities
                    .Where(m => m.UserId.ToString() == currentUserId && m.DeletedAt == null)
                    .Select(m => m.MentorId.ToString())
                    .FirstOrDefaultAsync() ?? string.Empty;

                // Create appeal
                Appeal appeal = new Appeal
                {
                    AppealId = Guid.NewGuid(),
                    TeamId = dto.TeamId,
                    TargetType = targetType,
                    TargetId = targetId,
                    OwnerId = student.UserId,
                    State = AppealStateEnum.Opened.ToString(),
                    Decision = AppealDecisionEnum.Pending.ToString(),
                    AppealResolution = dto.AppealResolution.ToString(),
                    Reason = dto.Reason.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = currentMentorId
                };

                await appealRepo.InsertAsync(appeal);
                await _unitOfWork.SaveAsync();

                // Upload evidence files if provided
                List<AppealEvidence> evidences = new List<AppealEvidence>();
                if (dto.Evidences != null && dto.Evidences.Any())
                {
                    foreach (AppealEvidenceFileDTO evidenceDto in dto.Evidences)
                    {
                        // Validate file type
                        if (!IsValidEvidenceFile(evidenceDto.File))
                        {
                            throw new ErrorException(StatusCodes.Status400BadRequest,
                                ResponseCodeConstants.BADREQUEST,
                                $"Invalid file type for evidence. Accepted types: PDF, images (jpg, jpeg, png).");
                        }

                        // Upload file
                        string fileUrl = await _cloudinaryService.UploadFileAsync(
                            evidenceDto.File,
                            APPEAL_EVIDENCE_FOLDER);

                        // Create evidence record
                        AppealEvidence evidence = new AppealEvidence
                        {
                            EvidenceId = Guid.NewGuid(),
                            AppealId = appeal.AppealId,
                            Url = fileUrl,
                            Note = evidenceDto.Note?.Trim(),
                            CreatedAt = DateTime.UtcNow
                        };

                        await evidenceRepo.InsertAsync(evidence);
                        evidences.Add(evidence);
                    }

                    await _unitOfWork.SaveAsync();
                }

                _unitOfWork.CommitTransaction();

                if (Guid.TryParse(currentUserId, out var requesterUserId))
                {
                    await _logWriter.TryWriteAsync(
                        requesterUserId,
                        ActivityActions.AppealSubmit,
                        TargetTypes.Appeal,
                        appeal.AppealId.ToString());
                }

                try
                {
                    var contestRepo = _unitOfWork.GetRepository<Contest>();
                    string? organizerId = await contestRepo.Entities
                        .Where(c => c.ContestId == round.ContestId && c.DeletedAt == null)
                        .Select(c => c.CreatedBy)
                        .FirstOrDefaultAsync();

                    if (Guid.TryParse(organizerId, out var organizerUserId))
                    {
                        // Notify organizer dashboard
                        await _dashboardNotifier.NotifyOrganizerDashboardUpdatedAsync(organizerUserId);

                        await _notificationService.CreateInAppToUserAsync(
                            organizerUserId,
                            NotificationTypes.AppealCreated,
                            new
                            {
                                appealId = appeal.AppealId,
                                teamId = appeal.TeamId,
                                roundId = appeal.TargetId,
                                contestId = round.ContestId,
                                state = appeal.State,
                                targetType = TargetTypes.Appeal,
                                targetId = appeal.AppealId.ToString(),
                                message = "New appeal submitted."
                            });
                    }
                }
                catch
                {
                }

                // Return created appeal
                return await GetAppealByIdAsync(appeal.AppealId);
            }
            catch (Exception ex)
            {
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating appeal: {ex.Message}");
            }
        }

        public async Task<GetAppealDTO> GetAppealByIdAsync(Guid appealId)
        {
            try
            {
                // Get repositories
                IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Get appeal
                Appeal? appeal = await appealRepo.Entities
                    .Where(a => a.AppealId == appealId && a.DeletedAt == null)
                    .Include(a => a.Team)
                        .ThenInclude(t => t.Mentor)
                            .ThenInclude(m => m.User)
                    .Include(a => a.Owner)
                    .Include(a => a.Target)
                        .ThenInclude(r => r.Contest)
                    .Include(a => a.AppealEvidences.Where(e => e.DeletedAt == null))
                    .FirstOrDefaultAsync();

                if (appeal == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Appeal not found.");
                }

                if (appeal.Target.IsRetakeRound)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Appeals are not allowed for retake rounds.");
                }

                // Get round information
                Guid roundId = appeal.TargetId;
                IGenericRepository<Round> roundRepo = _unitOfWork.GetRepository<Round>();
                Round? round = await roundRepo.Entities
                    .FirstOrDefaultAsync(r => r.RoundId == roundId && r.DeletedAt == null);

                // Map Appeal to GetAppealDTO
                GetAppealDTO result = _mapper.Map<GetAppealDTO>(appeal);

                // Set Mentor info
                result.MentorId = Guid.Parse(appeal.CreatedBy!);
                result.MentorName = appeal.Team.Mentor.User.Fullname;

                return result;
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving appeal: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetAppealDTO>> GetPaginatedAppealsAsync(
            int pageNumber,
            int pageSize,
            Guid? appealId,
            Guid? contestId,
            Guid? teamId,
            Guid? roundId,
            AppealStateEnum? state,
            AppealDecisionEnum? decision,
            bool isMyAppeals)
        {
            try
            {
                // Validate pagination parameters
                if (pageNumber < 1 || pageSize < 1)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Page number and page size must be greater than or equal to 1.");
                }

                // Get repositories
                IGenericRepository<Mentor> mentorRepo = _unitOfWork.GetRepository<Mentor>();
                IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();

                // Build base query
                IQueryable<Appeal> query = appealRepo.Entities
                    .Where(a => a.DeletedAt == null)
                    .Include(a => a.Team)
                    .Include(a => a.Owner)
                    .Include(a => a.Target)
                        .ThenInclude(r => r.Contest)
                    .Include(a => a.AppealEvidences.Where(e => e.DeletedAt == null));

                // Filter by current user's teams if requested
                if (isMyAppeals)
                {
                    // Get current user ID
                    string currentUserId = GetCurrentUserIdOrThrow();

                    // Get current mentor ID
                    string? currentMentorId = await mentorRepo.Entities
                        .Where(m => m.UserId.ToString() == currentUserId && m.DeletedAt == null)
                        .Select(m => m.MentorId.ToString().ToLower())
                        .FirstOrDefaultAsync();

                    // Filter appeals by mentor ID
                    query = query.Where(a => a.CreatedBy!.ToLower() == currentMentorId);
                }

                // Apply filters
                if (appealId.HasValue)
                {
                    query = query.Where(a => a.AppealId == appealId.Value);
                }

                if (contestId.HasValue)
                {
                    query = query.Where(a => a.Target.ContestId == contestId.Value);
                }

                if (teamId.HasValue)
                {
                    query = query.Where(a => a.TeamId == teamId.Value);
                }

                if (roundId.HasValue)
                {
                    query = query.Where(a => a.TargetId == roundId.Value);
                }

                if (state.HasValue)
                {
                    string stateString = state.Value.ToString();
                    query = query.Where(a => a.State == stateString);
                }

                if (decision.HasValue)
                {
                    string decString = decision.Value.ToString();
                    query = query.Where(a => a.Decision != null && (a.Decision == decString || a.Decision.StartsWith(decString + "\n")));
                }

                // Order by creation date (newest first)
                query = query.OrderByDescending(a => a.CreatedAt);

                // Get paginated results
                PaginatedList<Appeal> paginatedAppeals = await appealRepo.GetPagingAsync(query, pageNumber, pageSize);

                // Get list of mentor users for the appeals
                List<Mentor> mentorList = await mentorRepo.Entities
                    .Where(m => paginatedAppeals.Items
                        .Select(a => a.CreatedBy!.ToLower())
                        .Contains(m.MentorId.ToString().ToLower()))
                    .Include(m => m.User)
                    .ToListAsync();

                // Create a dictionary for quick lookup
                Dictionary<string, string> mentorDict = mentorList.ToDictionary(
                    m => m.MentorId.ToString().ToLower(),
                    m => m.User.Fullname);

                // Map to DTOs using AutoMapper
                IReadOnlyCollection<GetAppealDTO> items = paginatedAppeals.Items.Select(item =>
                {
                    // Map each Appeal to GetAppealDTO
                    GetAppealDTO dto = _mapper.Map<GetAppealDTO>(item);

                    // Set MentorId and MentorName
                    dto.MentorId = Guid.Parse(item.CreatedBy!);
                    dto.MentorName = mentorDict.ContainsKey(item.CreatedBy!.ToLower())
                        ? mentorDict[item.CreatedBy!.ToLower()]
                        : "Unknown Mentor";

                    return dto;
                }).ToList();

                return new PaginatedList<GetAppealDTO>(
                    items,
                    paginatedAppeals.TotalCount,
                    paginatedAppeals.PageNumber,
                    paginatedAppeals.PageSize);
            }
            catch (Exception ex)
            {
                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error retrieving appeals: {ex.Message}");
            }
        }

        public async Task<GetAppealDTO> ReviewAppealAsync(Guid appealId, ReviewAppealDTO dto)
        {
            try
            {
                _unitOfWork.BeginTransaction();

                // Validate user is an organizer
                string? userRole = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role);
                if (userRole != RoleConstants.ContestOrganizer)
                {
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        ResponseCodeConstants.FORBIDDEN,
                        "Only organizers can review appeals.");
                }

                IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();
                IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

                // Get appeal with related entities
                Appeal? appeal = await appealRepo.Entities
                    .Where(a => a.AppealId == appealId && a.DeletedAt == null)
                    .Include(a => a.Team)
                    .Include(a => a.Owner)
                    .Include(a => a.Target)
                        .ThenInclude(r => r.Problem)
                    .Include(a => a.Target)
                        .ThenInclude(r => r.McqTest)
                    .Include(a => a.Target)
                        .ThenInclude(r => r.Contest)
                    .FirstOrDefaultAsync();

                if (appeal == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Appeal not found.");
                }

                DateTime reviewDeadline = await GetAppealReviewDeadlineUtcAsync(appeal.Target, configRepo);
                DateTime reviewWindowStart = appeal.Target.End;
                if (DateTime.UtcNow < reviewWindowStart || DateTime.UtcNow > reviewDeadline)
                {
                    throw new ErrorException(StatusCodes.Status403Forbidden,
                        "APPEAL_REVIEW_DEADLINE_PASSED",
                        "Appeal review window is closed.");
                }

                // Check if already reviewed
                if (appeal.State == AppealStateEnum.Closed.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Appeal has already been reviewed.");
                }

                string contestStatus = appeal.Target.Contest.Status ?? string.Empty;

                // Check if contest is ongoing
                if (contestStatus != ContestStatusEnum.Ongoing.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Appeals can only be reviewed for rounds in ongoing contests.");
                }

                // Update appeal
                appeal.State = AppealStateEnum.Closed.ToString();
                appeal.Decision = dto.Decision;
                appeal.DecisionReason = dto.DecisionReason?.Trim() ?? string.Empty;

                // If approved, process approval logic
                if (dto.Decision == AppealDecisionEnum.Approved.ToString())
                {
                    AppealResolutionEnum? parsedResolution = null;
                    if (!string.IsNullOrEmpty(appeal.AppealResolution) &&
                        Enum.TryParse<AppealResolutionEnum>(appeal.AppealResolution, out var enumValue))
                    {
                        parsedResolution = enumValue;
                    }
                    await ProcessApprovedAppealLogicAsync(appeal, parsedResolution);
                }

                await appealRepo.UpdateAsync(appeal);
                await _unitOfWork.SaveAsync();

                _unitOfWork.CommitTransaction();

                string? reviewerId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (Guid.TryParse(reviewerId, out var reviewerUserId))
                {
                    await _logWriter.TryWriteAsync(
                        reviewerUserId,
                        ActivityActions.AppealResolve,
                        TargetTypes.Appeal,
                        appeal.AppealId.ToString());
                }

                try
                {
                    var notifyUserIds = new HashSet<Guid>();

                    if (appeal.OwnerId != Guid.Empty)
                    {
                        notifyUserIds.Add(appeal.OwnerId);
                    }

                    var mentorRepo = _unitOfWork.GetRepository<Mentor>();
                    if (appeal.Team?.MentorId != null)
                    {
                        Guid? mentorUserId = await mentorRepo.Entities
                            .Where(m => m.MentorId == appeal.Team.MentorId && m.DeletedAt == null)
                            .Select(m => (Guid?)m.UserId)
                            .FirstOrDefaultAsync();

                        if (mentorUserId.HasValue)
                        {
                            notifyUserIds.Add(mentorUserId.Value);
                        }
                    }

                    if (notifyUserIds.Count > 0)
                    {
                        await _notificationService.CreateInAppToUsersAsync(
                            notifyUserIds,
                            NotificationTypes.AppealUpdated,
                            new
                            {
                                appealId = appeal.AppealId,
                                teamId = appeal.TeamId,
                                roundId = appeal.TargetId,
                                contestId = appeal.Target.ContestId,
                                state = appeal.State,
                                decision = appeal.Decision,
                                targetType = TargetTypes.Appeal,
                                targetId = appeal.AppealId.ToString(),
                                message = "Appeal was updated."
                            });
                    }

                    if (string.Equals(appeal.Decision, AppealDecisionEnum.Approved.ToString(), StringComparison.OrdinalIgnoreCase)
                        && string.Equals(appeal.AppealResolution, AppealResolutionEnum.Rescore.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        var submissionRepo = _unitOfWork.GetRepository<Submission>();
                        var rescoreSubmission = await submissionRepo.Entities
                            .Where(s => s.TeamId == appeal.TeamId
                                        && s.Problem != null
                                        && s.Problem.RoundId == appeal.TargetId
                                        && s.DeletedAt == null)
                            .OrderByDescending(s => s.CreatedAt)
                            .Select(s => new { s.SubmissionId, s.JudgedBy })
                            .FirstOrDefaultAsync();

                        if (rescoreSubmission != null
                            && !string.IsNullOrWhiteSpace(rescoreSubmission.JudgedBy)
                            && Guid.TryParse(rescoreSubmission.JudgedBy, out var judgeUserId))
                        {
                            await _notificationService.CreateInAppToUserAsync(
                                judgeUserId,
                                NotificationTypes.ManualGradingAssigned,
                                new
                                {
                                    contestId = appeal.Target.ContestId,
                                    roundId = appeal.TargetId,
                                    submissionId = rescoreSubmission.SubmissionId,
                                    teamId = appeal.TeamId,
                                    appealId = appeal.AppealId,
                                    targetType = TargetTypes.Submission,
                                    targetId = rescoreSubmission.SubmissionId.ToString(),
                                    message = "Submission was reassigned for rescore."
                                });

                            var reviewerIdStr = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                            if (Guid.TryParse(reviewerIdStr, out var reviewerAssignId))
                            {
                                await _logWriter.TryWriteAsync(
                                    reviewerAssignId,
                                    ActivityActions.SubmissionAssignJudge,
                                    TargetTypes.Submission,
                                    rescoreSubmission.SubmissionId.ToString());
                            }
                        }
                    }
                }
                catch
                {
                }

                return await GetAppealByIdAsync(appealId);
            }
            catch (Exception ex)
            {
                _unitOfWork.RollBack();

                if (ex is ErrorException)
                {
                    throw;
                }

                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error reviewing appeal: {ex.Message}");
            }
        }

        private async Task ProcessApprovedAppealLogicAsync(Appeal appeal, AppealResolutionEnum? appealResolution)
        {
            if (appeal == null) return;

            Round? round = appeal.Target;
            string? problemType = round?.Problem?.Type;
            bool isMcq = round?.McqTest != null;
            bool isAutoEval = string.Equals(problemType, ProblemTypeEnum.AutoEvaluation.ToString(), StringComparison.OrdinalIgnoreCase);
            bool isManual = string.Equals(problemType, ProblemTypeEnum.Manual.ToString(), StringComparison.OrdinalIgnoreCase);

            // Determine and set appeal resolution
            if (isManual)
            {
                // For manual rounds, resolution must be specified
                if (!appealResolution.HasValue)
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Appeal resolution (Retake or Rescore) must be specified for manual problem type.");
                }

                appeal.AppealResolution = appealResolution.Value.ToString();

                // If Rescore, reassign submission to different judge and return
                if (appealResolution.Value == AppealResolutionEnum.Rescore)
                {
                    await ReassignSubmissionToNewJudgeAsync(appeal);
                    return;
                }

                if (appealResolution.Value == AppealResolutionEnum.RecheckPlagiarism)
                {
                    await RecheckSubmissionPlagiarismAsync(appeal);
                    return;
                }
            }
            else
            {
                // For MCQ or AutoEvaluation, default to Retake
                appeal.AppealResolution = AppealResolutionEnum.Retake.ToString();
            }

            // Get repositories
            IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
            IGenericRepository<McqAttempt> mcqAttemptRepo = _unitOfWork.GetRepository<McqAttempt>();

            Guid roundId = appeal.TargetId;

            // Get the student ID from OwnerId
            Guid? studentId = await studentRepo.Entities
                .Where(s => s.UserId == appeal.OwnerId && s.DeletedAt == null)
                .Select(s => (Guid?)s.StudentId)
                .FirstOrDefaultAsync();

            if (!studentId.HasValue)
                return;

            // Manual: mark the latest submission as cancelled
            if (isManual)
            {
                Submission? latest = await submissionRepo.Entities
                    .Where(s => s.Problem.RoundId == roundId
                                && s.SubmittedByStudentId == studentId.Value
                                && s.DeletedAt == null)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync();

                if (latest != null)
                {
                    latest.Status = SubmissionStatusEnum.Cancelled.ToString();
                    await submissionRepo.UpdateAsync(latest);
                }
            }
            // AutoEvaluation: mark all submissions as cancelled
            else if (isAutoEval)
            {
                List<Submission> subs = await submissionRepo.Entities
                    .Where(s => s.Problem.RoundId == roundId
                                && s.SubmittedByStudentId == studentId.Value
                                && s.DeletedAt == null)
                    .ToListAsync();

                foreach (Submission s in subs)
                {
                    s.Status = SubmissionStatusEnum.Cancelled.ToString();
                    await submissionRepo.UpdateAsync(s);
                }
            }
            // MCQ: mark mcq attempts as cancelled
            else if (isMcq)
            {
                List<McqAttempt> attempts = await mcqAttemptRepo.Entities
                    .Where(a => a.RoundId == roundId
                                && a.StudentId == studentId.Value
                                && a.DeletedAt == null)
                    .ToListAsync();

                foreach (McqAttempt at in attempts)
                {
                    at.Status = McqAttemptStatusEnum.Cancelled.ToString();
                    await mcqAttemptRepo.UpdateAsync(at);
                }
            }

            // Refresh the team score after data update
            Guid teamId = appeal.TeamId;
            await _leaderboardEntryService.UpdateTeamScoreAsync(round!.ContestId, teamId);
        }

        private async Task RecheckSubmissionPlagiarismAsync(Appeal appeal)
        {
            // Get the student's submission for this round
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
            IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();

            // Get the student ID from OwnerId
            Guid? studentId = await studentRepo.Entities
                .Where(s => s.UserId == appeal.OwnerId && s.DeletedAt == null)
                .Select(s => (Guid?)s.StudentId)
                .FirstOrDefaultAsync();

            if (!studentId.HasValue)
            {
                return;
            }

            // Find the latest submission for this student in the appeal's round
            Submission? submission = await submissionRepo.Entities
                .Where(s => s.Problem.RoundId == appeal.TargetId
                    && s.SubmittedByStudentId == studentId.Value
                    && !s.DeletedAt.HasValue)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            // If no submission found, nothing to recheck
            if (submission == null)
            {
                return;
            }

            // Check if submission status is PlagiarismConfirmed
            if (submission.Status == SubmissionStatusEnum.PlagiarismConfirmed.ToString())
            {
                // Change status to PlagiarismSuspected for rechecking
                submission.Status = SubmissionStatusEnum.PlagiarismSuspected.ToString();
                await submissionRepo.UpdateAsync(submission);
            }
        }

        private async Task ReassignSubmissionToNewJudgeAsync(Appeal appeal)
        {
            // Get the student's submission for this round
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();

            Submission? submission = await submissionRepo.Entities
                .Where(s => s.TeamId == appeal.TeamId
                    && s.Problem.RoundId == appeal.TargetId
                    && !s.DeletedAt.HasValue)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            // If no submission found, nothing to reassign
            if (submission == null)
            {
                return;
            }

            // Get current judge ID
            string? currentJudgeId = submission.JudgedBy;

            // Get all available judges for this contest
            IGenericRepository<JudgeInvite> judgeInviteRepo = _unitOfWork.GetRepository<JudgeInvite>();

            List<string> allJudges = await judgeInviteRepo.Entities
                .Where(ji => ji.ContestId == appeal.Target.ContestId
                    && ji.Status == JudgeInviteStatusEnum.Accepted.ToString().ToLower())
                .Select(ji => ji.JudgeId.ToString())
                .ToListAsync();

            // Determine the new judge
            string newJudgeId;

            if (allJudges.Count < 2)
            {
                // If there's only one judge, assign to the current judge
                newJudgeId = currentJudgeId ?? (allJudges.FirstOrDefault() ?? string.Empty);

                // If no judges available at all, return without reassigning
                if (string.IsNullOrEmpty(newJudgeId))
                {
                    return;
                }
            }
            else
            {
                // If there are multiple judges, exclude the current judge
                List<string> otherJudges = allJudges
                    .Where(j => j != currentJudgeId)
                    .ToList();

                if (otherJudges.Any())
                {
                    // Assign to a random different judge
                    Random random = new Random();
                    newJudgeId = otherJudges[random.Next(otherJudges.Count)];
                }
                else
                {
                    // Use current judge if filtering resulted in empty list
                    newJudgeId = currentJudgeId ?? allJudges.First();
                }
            }

            // Update submission
            submission.JudgedBy = newJudgeId;
            submission.Status = SubmissionStatusEnum.Pending.ToString();
            submission.Score = 0;

            await submissionRepo.UpdateAsync(submission);

            // Update judge deadline
            await UpsertJudgeRescoreDeadlineAsync(appeal.Target.ContestId, newJudgeId, submission.SubmissionId, configRepo);
        }

        private string GetCurrentUserIdOrThrow()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || !user.Identity?.IsAuthenticated == true)
            {
                throw new ErrorException(StatusCodes.Status401Unauthorized,
                    "UNAUTHENTICATED",
                    "Sign in required.");
            }

            string? id = user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ErrorException(StatusCodes.Status401Unauthorized,
                    "UNAUTHENTICATED",
                    "Invalid user context.");
            }

            return id;
        }

        private bool IsValidEvidenceFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return false;

            string[] allowedExtensions = { ".pdf", ".jpg", ".jpeg", ".png" };
            string extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            return allowedExtensions.Contains(extension);
        }

        private async Task<DateTime> GetAppealSubmitDeadlineUtcAsync(Round round, IGenericRepository<Config> configRepo)
        {
            DateTime? deadline = await TryGetRoundDeadlineUtcAsync(
                round.RoundId,
                ConfigKeys.RoundAppealSubmitDeadlineUtc(round.RoundId),
                configRepo);

            if (deadline.HasValue)
                return deadline.Value;

            int submitDays = await GetContestPolicyDaysAsync(
                round.ContestId, ContestPolicyKeys.AppealSubmitDays, DEFAULT_APPEAL_SUBMIT_DAYS, configRepo);
            return round.End.AddDays(submitDays);
        }

        private async Task<DateTime> GetAppealReviewDeadlineUtcAsync(Round round, IGenericRepository<Config> configRepo)
        {
            DateTime? deadline = await TryGetRoundDeadlineUtcAsync(
                round.RoundId,
                ConfigKeys.RoundAppealReviewDeadlineUtc(round.RoundId),
                configRepo);

            if (deadline.HasValue)
                return deadline.Value;

            int submitDays = await GetContestPolicyDaysAsync(
                round.ContestId, ContestPolicyKeys.AppealSubmitDays, DEFAULT_APPEAL_SUBMIT_DAYS, configRepo);
            int reviewDays = await GetContestPolicyDaysAsync(
                round.ContestId, ContestPolicyKeys.AppealReviewDays, DEFAULT_APPEAL_REVIEW_DAYS, configRepo);

            return round.End.AddDays(submitDays + reviewDays);
        }

        private static async Task<DateTime?> TryGetRoundDeadlineUtcAsync(
            Guid roundId,
            string key,
            IGenericRepository<Config> configRepo)
        {
            string? value = await configRepo.Entities
                .Where(c => c.Key == key && c.DeletedAt == null)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            if (value == null)
                return null;

            if (DateTime.TryParse(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out DateTime deadline))
            {
                return deadline;
            }

            return null;
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

        private async Task UpsertJudgeRescoreDeadlineAsync(
            Guid contestId,
            string judgeUserId,
            Guid submissionId,
            IGenericRepository<Config> configRepo)
        {
            if (!Guid.TryParse(judgeUserId, out var judgeId))
                return;

            // Load submission + round to compute extended rescore window
            var submissionRepo = _unitOfWork.GetRepository<Submission>();
            Submission? submission = await submissionRepo.Entities
                .Include(s => s.Problem)
                    .ThenInclude(p => p.Round)
                .FirstOrDefaultAsync(s => s.SubmissionId == submissionId && s.DeletedAt == null);

            if (submission?.Problem?.Round == null) return;
            var round = submission.Problem.Round;

            int judgeDays = await GetContestPolicyDaysAsync(
                contestId, ContestPolicyKeys.JudgeRescoreDays, DEFAULT_JUDGE_RESCORE_DAYS, configRepo);
            int submitDays = await GetContestPolicyDaysAsync(
                contestId, ContestPolicyKeys.AppealSubmitDays, DEFAULT_APPEAL_SUBMIT_DAYS, configRepo);
            int reviewDays = await GetContestPolicyDaysAsync(
                contestId, ContestPolicyKeys.AppealReviewDays, DEFAULT_APPEAL_REVIEW_DAYS, configRepo);

            // Extended window: round end + (judge*2 + submit + review) for appeals rescore
            DateTime deadline = round.End.AddDays(judgeDays * 2 + submitDays + reviewDays);
            string key = ConfigKeys.JudgeSubmissionDeadline(judgeId, submissionId);

            Config? existing = await configRepo.Entities.FirstOrDefaultAsync(c => c.Key == key);
            if (existing == null)
            {
                await configRepo.InsertAsync(new Config
                {
                    Key = key,
                    Value = deadline.ToString("o"),
                    Scope = "contest",
                    UpdatedAt = DateTime.UtcNow,
                    DeletedAt = null
                });
            }
            else
            {
                existing.Value = deadline.ToString("o");
                existing.Scope = "contest";
                existing.UpdatedAt = DateTime.UtcNow;
                existing.DeletedAt = null;
                configRepo.Update(existing);
            }

            await _unitOfWork.SaveAsync();
        }
    }
}
