using AutoMapper;
using BusinessLogic.IServices.Appeals;
using BusinessLogic.IServices.FileStorages;
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

        private const string APPEAL_EVIDENCE_FOLDER = "appeal_evidences";

        // Constructor
        public AppealService(
            IMapper mapper,
            IUOW uow,
            IHttpContextAccessor httpContextAccessor,
            ICloudinaryService cloudinaryService)
        {
            _mapper = mapper;
            _unitOfWork = uow;
            _httpContextAccessor = httpContextAccessor;
            _cloudinaryService = cloudinaryService;
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
                IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();

                Appeal? appeal = await appealRepo.Entities
                    .Where(a => a.AppealId == appealId && a.DeletedAt == null)
                    .Include(a => a.Team)
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

                IGenericRepository<Appeal> appealRepo = _unitOfWork.GetRepository<Appeal>();

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
                    string currentUserId = GetCurrentUserIdOrThrow();

                    IGenericRepository<Mentor> mentorRepo = _unitOfWork.GetRepository<Mentor>();
                    string? currentMentorId = await mentorRepo.Entities
                        .Where(m => m.UserId.ToString() == currentUserId && m.DeletedAt == null)
                        .Select(m => m.MentorId.ToString().ToLower())
                        .FirstOrDefaultAsync();

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

                // Map to DTOs using AutoMapper
                List<GetAppealDTO> items = _mapper.Map<List<GetAppealDTO>>(paginatedAppeals.Items);

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

                // Get appeal (include Target to inspect round type)
                Appeal? appeal = await appealRepo.Entities
                    .Where(a => a.AppealId == appealId && a.DeletedAt == null)
                    .Include(a => a.Team)
                    .Include(a => a.Owner)
                    .Include(a => a.Target)
                        .ThenInclude(r => r.Problem)
                    .Include(a => a.Target)
                        .ThenInclude(r => r.McqTest)
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
                appeal.DecisionReason += dto.DecisionReason?.Trim() ?? string.Empty;

                await appealRepo.UpdateAsync(appeal);

                // If approved, process related soft-deletes and config updates
                if (dto.Decision == AppealDecisionEnum.Approved.ToString())
                {
                    await ProcessApprovedAppealAsync(appeal);
                }

                await _unitOfWork.SaveAsync();
                _unitOfWork.CommitTransaction();

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

        private async Task ProcessApprovedAppealAsync(Appeal appeal)
        {
            if (appeal == null) return;

            IGenericRepository<Config> configRepo = _unitOfWork.GetRepository<Config>();
            IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
            IGenericRepository<Submission> submissionRepo = _unitOfWork.GetRepository<Submission>();
            IGenericRepository<SubmissionArtifact> artifactRepo = _unitOfWork.GetRepository<SubmissionArtifact>();
            IGenericRepository<SubmissionDetail> detailRepo = _unitOfWork.GetRepository<SubmissionDetail>();
            IGenericRepository<McqAttempt> mcqAttemptRepo = _unitOfWork.GetRepository<McqAttempt>();

            Guid roundId = appeal.TargetId;

            // Get the student ID from OwnerId
            Guid? studentId = await studentRepo.Entities
                .Where(s => s.UserId == appeal.OwnerId && s.DeletedAt == null)
                .Select(s => (Guid?)s.StudentId)
                .FirstOrDefaultAsync();

            if (!studentId.HasValue)
                return;

            // Soft delete finished mark for this specific student only
            string finishKey = ConfigKeys.RoundStudent(roundId, studentId.Value);

            Config? finishConfig = await configRepo.Entities
                .FirstOrDefaultAsync(c => c.Key == finishKey);

            if (finishConfig != null)
            {
                configRepo.Delete(finishConfig);
            }

            // Determine round type
            Round? round = appeal.Target;
            string? problemType = round?.Problem?.Type;
            bool isMcq = round?.McqTest != null;
            bool isAutoEval = string.Equals(problemType, ProblemTypeEnum.AutoEvaluation.ToString(), StringComparison.OrdinalIgnoreCase);
            bool isManual = string.Equals(problemType, ProblemTypeEnum.Manual.ToString(), StringComparison.OrdinalIgnoreCase);

            // Manual: soft delete the latest submission for this student in the round
            if (isManual)
            {
                Submission? latest = await submissionRepo.Entities
                    .Include(s => s.SubmissionArtifacts)
                    .Include(s => s.SubmissionDetails)
                    .Where(s => s.Problem.RoundId == roundId
                                && s.SubmittedByStudentId == studentId.Value
                                && s.DeletedAt == null)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync();

                if (latest != null)
                {
                    latest.DeletedAt = DateTime.UtcNow;
                    await submissionRepo.UpdateAsync(latest);

                    // Soft delete related artifacts & details
                    foreach (SubmissionArtifact art in latest.SubmissionArtifacts)
                    {
                        if (art.DeletedAt == null)
                        {
                            art.DeletedAt = DateTime.UtcNow;
                            await artifactRepo.UpdateAsync(art);
                        }
                    }

                    foreach (SubmissionDetail det in latest.SubmissionDetails)
                    {
                        if (det.DeletedAt == null)
                        {
                            det.DeletedAt = DateTime.UtcNow;
                            await detailRepo.UpdateAsync(det);
                        }
                    }
                }
            }
            // AutoEvaluation: soft delete all submissions for this student in the round
            else if (isAutoEval)
            {
                List<Submission> subs = await submissionRepo.Entities
                    .Include(s => s.SubmissionArtifacts)
                    .Include(s => s.SubmissionDetails)
                    .Where(s => s.Problem.RoundId == roundId
                                && s.SubmittedByStudentId == studentId.Value
                                && s.DeletedAt == null)
                    .ToListAsync();

                foreach (Submission s in subs)
                {
                    s.DeletedAt = DateTime.UtcNow;
                    await submissionRepo.UpdateAsync(s);

                    foreach (SubmissionArtifact art in s.SubmissionArtifacts)
                    {
                        if (art.DeletedAt == null)
                        {
                            art.DeletedAt = DateTime.UtcNow;
                            await artifactRepo.UpdateAsync(art);
                        }
                    }

                    foreach (SubmissionDetail det in s.SubmissionDetails)
                    {
                        if (det.DeletedAt == null)
                        {
                            det.DeletedAt = DateTime.UtcNow;
                            await detailRepo.UpdateAsync(det);
                        }
                    }
                }
            }
            // MCQ: soft delete mcq attempts for this student and round
            else if (isMcq)
            {
                List<McqAttempt> attempts = await mcqAttemptRepo.Entities
                    .Where(a => a.RoundId == roundId
                                && a.StudentId == studentId.Value
                                && a.DeletedAt == null)
                    .ToListAsync();

                foreach (McqAttempt at in attempts)
                {
                    at.DeletedAt = DateTime.UtcNow;
                    await mcqAttemptRepo.UpdateAsync(at);
                }
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
