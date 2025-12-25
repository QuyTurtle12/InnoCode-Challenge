using AutoMapper;
using BusinessLogic.IServices.Appeals;
using BusinessLogic.IServices.Contests;
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

        private const string APPEAL_EVIDENCE_FOLDER = "appeal_evidences";

        // Constructor
        public AppealService(
            IMapper mapper,
            IUOW uow,
            IHttpContextAccessor httpContextAccessor,
            ICloudinaryService cloudinaryService,
            ILeaderboardEntryService leaderboardEntryService,
            INotificationService notificationService,
            IActivityLogWriter logWriter)
        {
            _mapper = mapper;
            _unitOfWork = uow;
            _httpContextAccessor = httpContextAccessor;
            _cloudinaryService = cloudinaryService;
            _leaderboardEntryService = leaderboardEntryService;
            _notificationService = notificationService;
            _logWriter = logWriter;
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
                IGenericRepository<AppealEvidence> evidenceRepo = _unitOfWork.GetRepository<AppealEvidence>();

                // Validate round exists
                Round? round = await roundRepo.Entities
                    .Where(r => r.RoundId == dto.RoundId && r.DeletedAt == null)
                    .Include(r => r.McqTest)
                    .Include(r => r.Problem)
                    .FirstOrDefaultAsync();

                if (round == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Round not found.");
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

                // Get appeal
                Appeal? appeal = await appealRepo.Entities
                    .Where(a => a.AppealId == appealId && a.DeletedAt == null)
                    .Include(a => a.Team)
                        .ThenInclude(t => t.Mentor)
                            .ThenInclude(m => m.User)
                    .Include(a => a.Owner)
                    .Include(a => a.Target)
                    .Include(a => a.AppealEvidences.Where(e => e.DeletedAt == null))
                    .FirstOrDefaultAsync();

                if (appeal == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Appeal not found.");
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

                // Get appeal with related entities
                Appeal? appeal = await appealRepo.Entities
                    .Where(a => a.AppealId == appealId && a.DeletedAt == null)
                    .Include(a => a.Team)
                    .Include(a => a.Owner)
                    .Include(a => a.Target)
                        .ThenInclude(r => r.Problem)
                    .Include(a => a.Target)
                        .ThenInclude(r => r.McqTest)
                    .Include(a => a.Target.Contest)
                    .FirstOrDefaultAsync();

                if (appeal == null)
                {
                    throw new ErrorException(StatusCodes.Status404NotFound,
                        ResponseCodeConstants.NOT_FOUND,
                        "Appeal not found.");
                }

                // Check if already reviewed
                if (appeal.State == AppealStateEnum.Closed.ToString())
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest,
                        ResponseCodeConstants.BADREQUEST,
                        "Appeal has already been reviewed.");
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

        private async Task ReassignSubmissionToNewJudgeAsync(Appeal appeal)
        {
            // Get the student's submission for this round
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();

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

            // Get available judges for this contest (excluding current judge)
            IGenericRepository<JudgeInvite> judgeInviteRepo = _unitOfWork.GetRepository<JudgeInvite>();

            List<string> availableJudges = await judgeInviteRepo.Entities
                .Where(ji => ji.ContestId == appeal.Target.ContestId
                    && ji.Status == JudgeInviteStatusEnum.Accepted.ToString().ToLower()
                    && ji.JudgeId.ToString() != currentJudgeId)
                .Select(ji => ji.JudgeId.ToString())
                .ToListAsync();

            if (availableJudges.Any())
            {
                // Assign to a random different judge
                Random random = new Random();
                string newJudgeId = availableJudges[random.Next(availableJudges.Count)];

                submission.JudgedBy = newJudgeId;
                submission.Status = SubmissionStatusEnum.Pending.ToString();
                submission.Score = 0;

                await submissionRepo.UpdateAsync(submission);
            }
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
    }
}
