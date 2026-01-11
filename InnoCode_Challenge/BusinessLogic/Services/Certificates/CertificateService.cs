using AutoMapper;
using BusinessLogic.IServices.Certificates;
using BusinessLogic.IServices.Dashboards;
using BusinessLogic.IServices.FileStorages;
using BusinessLogic.IServices.NotificationsAndLogs;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repository.DTOs.CertificateDTOs;
using Repository.DTOs.CertificateTemplateDTOs;
using Repository.IRepositories;
using System.Security.Claims;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Certificates
{
    public class CertificateService : ICertificateService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private readonly ICloudinaryService _cloud;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<CertificateService> _logger;
        private readonly INotificationService _notificationService;
        private readonly IActivityLogWriter _logWriter;
        private readonly IDashboardNotifierService _dashboardNotifier;

        public CertificateService(
            IMapper mapper,
            IUOW unitOfWork,
            ICloudinaryService cloud,
            IHttpClientFactory httpClientFactory,
            ILogger<CertificateService> logger,
            IHttpContextAccessor httpContextAccessor,
            INotificationService notificationService,
            IActivityLogWriter logWriter,
            IDashboardNotifierService dashboardNotifier)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
            _cloud = cloud;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _notificationService = notificationService;
            _logWriter = logWriter;
            _dashboardNotifier = dashboardNotifier;
        }

        public async Task<IReadOnlyList<IssuedCertificateDTO>> IssueAsync(IssueCertificatesDTO dto)
        {
            _logger.LogInformation("Starting certificate issuance for TemplateId={TemplateId}, RecipientCount={RecipientCount}, Reissue={Reissue}", 
                dto.TemplateId, dto.Recipients?.Count ?? 0, dto.Reissue);

            try
            {
                // Get repositories
                IGenericRepository<CertificateTemplate> tplRepo = _unitOfWork.GetRepository<CertificateTemplate>();
                IGenericRepository<Certificate> certRepo = _unitOfWork.GetRepository<Certificate>();
                IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
                IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();

                // Fetch the template
                _logger.LogDebug("Fetching certificate template with TemplateId={TemplateId}", dto.TemplateId);
                CertificateTemplate? tpl = await tplRepo.Entities.Include(t => t.Contest).FirstOrDefaultAsync(t => t.TemplateId == dto.TemplateId && t.DeletedAt == null);

                if (tpl == null)
                {
                    _logger.LogWarning("Certificate template not found. TemplateId={TemplateId}", dto.TemplateId);
                    throw new ErrorException(StatusCodes.Status404NotFound, CertificateErrorCodeConstants.TemplateNotFound, $"No template with ID={dto.TemplateId}");
                }

                await EnsureContestOwnedByOrganizerOrAdminAsync(tpl.ContestId);

                _logger.LogDebug("Template found: {TemplateName}, FileUrl={FileUrl}", tpl.Name, tpl.FileUrl);

                // Download the template file
                try
                {
                    _logger.LogDebug("Downloading template file from {FileUrl}", tpl.FileUrl);
                    HttpClient? http = _httpClientFactory.CreateClient();
                    using var response = await http.GetAsync(tpl.FileUrl!);
                    _logger.LogInformation(
                        "Template download response. Status={StatusCode}, ContentType={ContentType}, ContentLength={ContentLength}",
                        (int)response.StatusCode,
                        response.Content.Headers.ContentType?.ToString() ?? "unknown",
                        response.Content.Headers.ContentLength?.ToString() ?? "unknown");

                    response.EnsureSuccessStatusCode();

                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    using MemoryStream templateStream = new MemoryStream(bytes);

                    _logger.LogInformation("Template file downloaded successfully. Size={FileSize} bytes", bytes.Length);

                    // Prepare results list
                    List<IssuedCertificateDTO>? results = new List<IssuedCertificateDTO>();
                    Guid? issuerUserId = TryGetCurrentUserId();

                    // Issue certificates to each recipient
                    for (int i = 0; i < dto.Recipients.Count; i++)
                    {
                        
                        IssueRecipientDTO r = dto.Recipients[i];
                        _logger.LogDebug("Processing recipient {Index}/{Total}: StudentId={StudentId}, TeamId={TeamId}", 
                            i + 1, dto.Recipients.Count, r.StudentId, r.TeamId);

                        try
                        {
                            bool isStudent = r.StudentId.HasValue;
                            bool isTeam = r.TeamId.HasValue;

                            // Validate recipient specification
                            if (isStudent == isTeam)
                            {
                                _logger.LogError("Invalid recipient specification at index {Index}: StudentId={StudentId}, TeamId={TeamId}. Must specify exactly one.", 
                                    i, r.StudentId, r.TeamId);
                                throw new ErrorException(StatusCodes.Status400BadRequest, "RECIPIENT_INVALID",
                                    "Specify exactly one of studentId or teamId.");
                            }

                            string certType = isStudent ? CertificateTypeConstants.Student : CertificateTypeConstants.Team;

                            // Fetch recipient details
                            string recipientName;
                            Guid? studentId = null; Guid? teamId = null;

                            // Validate and get student or team
                            if (isStudent)
                            {
                                // Fetch student
                                _logger.LogDebug("Fetching student with StudentId={StudentId}", r.StudentId);
                                Student? stu = await studentRepo.Entities.Include(s => s.User)
                                    .FirstOrDefaultAsync(s => s.StudentId == r.StudentId && s.DeletedAt == null);

                                // Validate student existence
                            if (stu == null)
                            {
                                _logger.LogWarning("Student not found. StudentId={StudentId}", r.StudentId);
                                throw new ErrorException(StatusCodes.Status404NotFound,
                                    CertificateErrorCodeConstants.StudentNotFound,
                                    $"No student with ID={r.StudentId}");
                            }

                            bool isStudentInContest = await teamMemberRepo.Entities
                                .Include(tm => tm.Team)
                                .AnyAsync(tm => tm.StudentId == stu.StudentId &&
                                                tm.Team.DeletedAt == null &&
                                                tm.Team.ContestId == tpl.ContestId);

                            if (!isStudentInContest)
                            {
                                throw new ErrorException(StatusCodes.Status400BadRequest,
                                    ResponseCodeConstants.BADREQUEST,
                                    "Student does not belong to this contest.");
                            }

                            studentId = stu.StudentId;
                            recipientName = r.DisplayName?.Trim() ?? (stu.User?.Fullname ?? "Student");
                            _logger.LogDebug("Student found: {StudentName}", recipientName);
                            }
                            else
                            {
                                // Fetch team
                                _logger.LogDebug("Fetching team with TeamId={TeamId}", r.TeamId);
                                Team? tm = await teamRepo.Entities.FirstOrDefaultAsync(t => t.TeamId == r.TeamId && t.DeletedAt == null);

                                // Validate team existence
                                if (tm == null)
                                {
                                    _logger.LogWarning("Team not found. TeamId={TeamId}", r.TeamId);
                                    throw new ErrorException(StatusCodes.Status404NotFound,
                                        CertificateErrorCodeConstants.TeamNotFound,
                                        $"No team with ID={r.TeamId}");
                                }

                                if (tm.ContestId != tpl.ContestId)
                                {
                                    throw new ErrorException(StatusCodes.Status400BadRequest,
                                        ResponseCodeConstants.BADREQUEST,
                                        "Team does not belong to this contest.");
                                }

                                // Set team details
                                teamId = tm.TeamId;
                                recipientName = r.DisplayName?.Trim() ?? tm.Name;
                                _logger.LogDebug("Team found: {TeamName}", recipientName);
                            }

                            // Check for duplicate certificate if not reissuing
                            if (!dto.Reissue)
                            {
                                _logger.LogDebug("Checking for duplicate certificate. TemplateId={TemplateId}, StudentId={StudentId}, TeamId={TeamId}", 
                                    tpl.TemplateId, studentId, teamId);
                                bool exists = await certRepo.Entities.AnyAsync(c =>
                                    c.TemplateId == tpl.TemplateId &&
                                    c.StudentId == studentId &&
                                    c.CertificateType == certType &&
                                    c.TeamId == teamId &&
                                    c.DeletedAt == null);
                                if (exists)
                                {
                                    throw new ErrorException(StatusCodes.Status409Conflict,
                                        CertificateErrorCodeConstants.DuplicateCertificate,
                                        "Certificate already exists for this recipient and template.");
                                }
                            }

                            // Render text on the certificate template
                            _logger.LogDebug("Rendering certificate for {RecipientName}", recipientName);
                            TextLayoutDTO layout = new TextLayoutDTO
                            {
                                X = (int)(tpl.TextX ?? 960),
                                Y = (int)(tpl.TextY ?? 540),

                                FontFamily = "Arial",
                                FontSize = 64f,
                                ColorHex = "#1F2937",
                                MaxWidth = 1600,
                                Align = "center"
                            };

                            byte[] pngBytes;
                            try
                            {
                                pngBytes = ImageDrawHelper.RenderTextOnImage(
                                    template: templateStream,
                                    displayText: recipientName,
                                    fontFamily: layout.FontFamily,
                                    fontSize: layout.FontSize,
                                    colorHex: layout.ColorHex,
                                    x: layout.X, y: layout.Y, maxWidth: layout.MaxWidth, align: layout.Align);
                                templateStream.Position = 0;
                                _logger.LogDebug("Certificate rendered successfully. Size={Size} bytes", pngBytes.Length);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to render certificate image for {RecipientName}. TemplateId={TemplateId}", 
                                    recipientName, tpl.TemplateId);
                                throw new ErrorException(StatusCodes.Status500InternalServerError,
                                    "RENDER_FAILED",
                                    "Failed to render certificate image.");
                            }

                            // Upload the rendered certificate to cloud storage
                            string fileName = $"cert_{tpl.TemplateId}_{studentId?.ToString() ?? teamId!.ToString()}_{DateTime.UtcNow:yyyyMMddHHmmss}.png";
                            _logger.LogDebug("Uploading certificate to cloud storage. FileName={FileName}", fileName);

                            // Upload to cloud
                            string url;
                            try
                            {
                                using var mem = new MemoryStream(pngBytes);
                                url = await _cloud.UploadImageAsync(mem, "certificates", fileName);
                                _logger.LogInformation("Certificate uploaded successfully. FileName={FileName}, Url={Url}", fileName, url);
                            }
                            catch (ErrorException ex)
                            {
                                _logger.LogError(ex, "Cloudinary upload failed for {FileName}", fileName);
                                throw new ErrorException(StatusCodes.Status500InternalServerError,
                                    CertificateErrorCodeConstants.StorageUploadFailed,
                                    "Failed to upload certificate file.");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Unexpected error when uploading {FileName}", fileName);
                                throw new ErrorException(StatusCodes.Status500InternalServerError,
                                    CertificateErrorCodeConstants.StorageUploadFailed,
                                    "Failed to upload certificate file.");
                            }


                            // Create or update the certificate record
                            Certificate? entity;
                            if (dto.Reissue)
                            {
                                _logger.LogDebug("Reissue mode: checking for existing certificate");
                                // Fetch existing certificate
                                
                                entity = await certRepo.Entities.FirstOrDefaultAsync(c =>
                                    c.TemplateId == tpl.TemplateId &&
                                    c.CertificateType == certType &&                 
                                    c.StudentId == studentId &&
                                    c.TeamId == teamId &&
                                    c.DeletedAt == null);

                                // If not found, create new
                                if (entity == null)
                                {
                                    _logger.LogDebug("Existing certificate not found. Creating new certificate record.");
                                    entity = new Certificate
                                    {
                                        CertificateId = Guid.NewGuid(),
                                        TemplateId = tpl.TemplateId,
                                        StudentId = studentId,
                                        TeamId = teamId,
                                        CertificateType = certType,
                                        FileUrl = url,
                                        IssuedAt = DateTime.UtcNow
                                    };
                                    await certRepo.InsertAsync(entity);
                                }
                                // Update existing
                                else
                                {
                                    _logger.LogDebug("Existing certificate found. Updating CertificateId={CertificateId}", entity.CertificateId);
                                    entity.FileUrl = url;
                                    entity.IssuedAt = DateTime.UtcNow;

                                    // Ensure CertificateType is set
                                    if (string.IsNullOrWhiteSpace(entity.CertificateType))
                                        entity.CertificateType = certType;

                                    certRepo.Update(entity);
                                }
                            }
                            // New issuance
                            else
                            {
                                _logger.LogDebug("Creating new certificate record");
                                entity = new Certificate
                                {
                                    CertificateId = Guid.NewGuid(),
                                    TemplateId = tpl.TemplateId,
                                    StudentId = studentId,
                                    TeamId = teamId,
                                    CertificateType = certType,
                                    FileUrl = url,
                                    IssuedAt = DateTime.UtcNow
                                };
                                await certRepo.InsertAsync(entity);
                            }

                            // Save changes
                            try
                            {
                                await _unitOfWork.SaveAsync();
                                _logger.LogInformation("Certificate saved successfully. CertificateId={CertificateId}, Recipient={RecipientName}", 
                                    entity.CertificateId, recipientName);

                                // Notify dashboard about new certificate
                                await _dashboardNotifier.NotifyCertificateIssuedAsync();

                                // Notify mentor if team certificate
                                if (entity.TeamId.HasValue)
                                {
                                    Guid contestId = tpl.ContestId;
                                    await NotifiMentorDashboardContestUpdated(contestId);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to save certificate to database. CertificateId={CertificateId}", entity.CertificateId);
                                throw new ErrorException(StatusCodes.Status500InternalServerError,
                                    "SAVE_FAILED",
                                    "Failed to save certificate record.");
                            }

                            // Add to results
                            results.Add(new IssuedCertificateDTO
                            {
                                CertificateId = entity.CertificateId,
                                TemplateId = entity.TemplateId,
                                TeamId = entity.TeamId,
                                StudentId = entity.StudentId,
                                RecipientName = recipientName,
                                CertificateType = entity.CertificateType ?? certType,
                                FileUrl = entity.FileUrl,
                                IssuedAt = entity.IssuedAt
                            });

                            if (issuerUserId.HasValue)
                            {
                                await _logWriter.TryWriteAsync(
                                    issuerUserId.Value,
                                    ActivityActions.CertificateIssue,
                                    TargetTypes.Certificate,
                                    entity.CertificateId.ToString());
                            }

                            try
                            {
                                if (studentId.HasValue)
                                {
                                    Guid studentUserId = await studentRepo.Entities
                                        .Where(s => s.StudentId == studentId.Value && s.DeletedAt == null)
                                        .Select(s => s.UserId)
                                        .FirstOrDefaultAsync();

                                    if (studentUserId != Guid.Empty)
                                    {
                                        await _notificationService.CreateInAppToUserAsync(
                                            studentUserId,
                                            NotificationTypes.CertificateIssued,
                                            new
                                            {
                                                certificateId = entity.CertificateId,
                                                templateId = entity.TemplateId,
                                                contestId = tpl.ContestId,
                                                certificateType = entity.CertificateType,
                                                targetType = TargetTypes.Certificate,
                                                targetId = entity.CertificateId.ToString(),
                                                message = "Certificate issued."
                                            });
                                    }
                                }
                                else if (teamId.HasValue)
                                {
                                    var studentIds = await _unitOfWork.GetRepository<TeamMember>().Entities
                                        .Where(tm => tm.TeamId == teamId.Value)
                                        .Select(tm => tm.StudentId)
                                        .ToListAsync();

                                    var userIds = await studentRepo.Entities
                                        .Where(s => studentIds.Contains(s.StudentId) && s.DeletedAt == null)
                                        .Select(s => s.UserId)
                                        .ToListAsync();

                                    if (userIds.Count > 0)
                                    {
                                        await _notificationService.CreateInAppToUsersAsync(
                                            userIds,
                                            NotificationTypes.CertificateIssued,
                                            new
                                            {
                                                certificateId = entity.CertificateId,
                                                templateId = entity.TemplateId,
                                                contestId = tpl.ContestId,
                                                certificateType = entity.CertificateType,
                                                targetType = TargetTypes.Certificate,
                                                targetId = entity.CertificateId.ToString(),
                                                message = "Certificate issued."
                                            });
                                    }
                                }
                            }
                            catch
                            {
                            }

                            templateStream.Position = 0;
                        }
                        catch (ErrorException)
                        {
                            // Re-throw ErrorException without wrapping
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Unexpected error processing recipient at index {Index}. StudentId={StudentId}, TeamId={TeamId}", 
                                i, r.StudentId, r.TeamId);
                            throw new ErrorException(StatusCodes.Status500InternalServerError,
                                "RECIPIENT_PROCESSING_FAILED",
                                $"Failed to process recipient at index {i}.");
                        }
                    }

                    _logger.LogInformation("Certificate issuance completed successfully. TotalIssued={TotalIssued}", results.Count);
                    return results;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Failed to download template file. FileUrl={FileUrl}", tpl?.FileUrl);
                    throw new ErrorException(StatusCodes.Status500InternalServerError,
                        "TEMPLATE_DOWNLOAD_FAILED",
                        "Failed to download certificate template file.");
                }
            }
            catch (ErrorException)
            {
                // Re-throw ErrorException without additional logging (already logged)
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during certificate issuance. TemplateId={TemplateId}", dto.TemplateId);
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    "ISSUANCE_FAILED",
                    "An unexpected error occurred during certificate issuance.");
            }
        }

        public async Task<CertificateDTO?> GetByIdAsync(Guid id)
        {
            // Get the repository
            IGenericRepository<Certificate> repo = _unitOfWork.GetRepository<Certificate>();

            // Fetch the certificate by ID
            Certificate? certificate = await repo.Entities
                .Include(x => x.Template).ThenInclude(t => t.Contest)
                .Include(x => x.Team)
                .Include(x => x.Student).ThenInclude(s => s.User)
                .FirstOrDefaultAsync(x => x.CertificateId == id && x.DeletedAt == null);

            // If not found, return null
            if (certificate == null)
            {
                return null;
            }

            await EnsureContestOwnedByOrganizerOrAdminAsync(certificate.Template.ContestId);

            // Map to DTO and return
            return new CertificateDTO
            {
                CertificateId = certificate.CertificateId,
                TemplateId = certificate.TemplateId,
                TemplateName = certificate.Template.Name,
                ContestId = certificate.Template.ContestId,
                TeamId = certificate.TeamId,
                TeamName = certificate.Team?.Name,
                StudentId = certificate.StudentId,
                StudentName = certificate.Student?.User?.Fullname,
                CertificateType = certificate.CertificateType ?? (certificate.StudentId != null ? CertificateTypeConstants.Student : CertificateTypeConstants.Team),

                FileUrl = certificate.FileUrl,
                IssuedAt = certificate.IssuedAt
            };
        }

        public async Task<PaginatedList<CertificateDTO>> GetAsync(
            Guid? contestId,
            Guid? templateId,
            Guid? teamId,
            Guid? studentId,
            string? types,
            int page,
            int pageSize,
            string? sortBy,
            bool desc,
            bool myCertificate)
        {
            // Get the repository
            IGenericRepository<Certificate> repo = _unitOfWork.GetRepository<Certificate>();

            // Build the query
            IQueryable<Certificate> q = repo.Entities
                .Where(c => c.DeletedAt == null)
                .Include(c => c.Template).ThenInclude(t => t.Contest)
                .Include(c => c.Team)
                .Include(c => c.Student).ThenInclude(s => s.User);

            // Filter by current user's certificates
            if (myCertificate)
            {
                // Get current user ID and role from HTTP context
                string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                string? userRole = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role);

                // If user ID is not found, throw an unauthorized error
                if (string.IsNullOrWhiteSpace(userId))
                {
                    throw new ErrorException(StatusCodes.Status401Unauthorized,
                        ResponseCodeConstants.UNAUTHORIZED,
                        "User ID not found.");
                }

                // If user role is not found, throw an unauthorized error
                if (string.IsNullOrWhiteSpace(userRole))
                {
                    throw new ErrorException(StatusCodes.Status401Unauthorized,
                        ResponseCodeConstants.UNAUTHORIZED,
                        "User role not found.");
                }

                // If the user is a student, filter certificates accordingly
                if (userRole.Equals(RoleConstants.Student))
                {
                    // Parse user ID to GUID
                    Guid.TryParse(userId, out Guid userGuid);

                    // Get the current student's ID
                    IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();

                    // Find the student associated with the current user
                    Guid? currentStudentId = await studentRepo.Entities
                        .Where(s => s.UserId == userGuid && s.DeletedAt == null)
                        .Select(s => (Guid?)s.StudentId)
                        .FirstOrDefaultAsync();
                    if (currentStudentId == null)
                        throw new ErrorException(StatusCodes.Status404NotFound, "STUDENT_NOT_FOUND", "Student profile not found.");

                    IGenericRepository<TeamMember> teamMemberRepo = _unitOfWork.GetRepository<TeamMember>();
                    // Get team IDs for the current student
                    List<Guid> myTeamIds = await teamMemberRepo.Entities
                        .Where(tm => tm.StudentId == currentStudentId.Value)
                        .Select(tm => tm.TeamId)
                        .Distinct()
                        .ToListAsync();

                    // Filter certificates for the current student or their teams
                    q = q.Where(c =>
                        c.StudentId == currentStudentId.Value ||
                        (c.TeamId != null && myTeamIds.Contains(c.TeamId.Value)));
                }

                if (userRole.Equals(RoleConstants.Mentor))
                {
                    // Parse user ID to GUID
                    Guid.TryParse(userId, out Guid userGuid);

                    // Get the current mentor's ID
                    IGenericRepository<Mentor> mentorRepo = _unitOfWork.GetRepository<Mentor>();

                    // Find the mentor associated with the current user
                    Guid? currentMentorId = await mentorRepo.Entities
                        .Where(m => m.UserId == userGuid && m.DeletedAt == null)
                        .Select(m => (Guid?)m.MentorId)
                        .FirstOrDefaultAsync();

                    if (currentMentorId == null)
                        throw new ErrorException(StatusCodes.Status404NotFound, "MENTOR_NOT_FOUND", "Mentor profile not found.");

                    // Get team IDs managed by the current mentor
                    IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();
                    List<Guid> managedTeamIds = await teamRepo.Entities
                        .Where(t => t.MentorId == currentMentorId.Value && t.DeletedAt == null)
                        .Select(t => t.TeamId)
                        .Distinct()
                        .ToListAsync();

                    // Filter certificates for teams managed by this mentor
                    q = q.Where(c =>
                        c.TeamId != null &&
                        managedTeamIds.Contains(c.TeamId.Value) &&
                        (c.CertificateType == CertificateTypeConstants.Team ||
                         (c.CertificateType == null && c.TeamId != null)));
                }

            }
            else
            {
                if (contestId.HasValue)
                {
                    await EnsureContestOwnedByOrganizerOrAdminAsync(contestId.Value);
                }
                else if (!IsAdmin())
                {
                    var userId = GetCurrentUserIdOrThrow();
                    q = q.Where(c => c.Template.Contest.CreatedBy == userId.ToString());
                }
            }

            // Apply filters
            if (templateId.HasValue) q = q.Where(c => c.TemplateId == templateId.Value);
            if (contestId.HasValue) q = q.Where(c => c.Template.ContestId == contestId.Value);
            if (teamId.HasValue) q = q.Where(c => c.TeamId == teamId.Value);
            if (studentId.HasValue) q = q.Where(c => c.StudentId == studentId.Value);

            // Filter by certificate types
            if (!string.IsNullOrWhiteSpace(types))
            {
                var parts = types.Split(new[] { ',', ';', '|', ' ' },
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.ToLowerInvariant())
                    .ToHashSet();

                bool wantStudent = parts.Contains("student");
                bool wantTeam = parts.Contains("team");

                if (!wantStudent && !wantTeam)
                {
                    throw new ErrorException(
                        StatusCodes.Status400BadRequest,
                        "CERTIFICATE_TYPE_INVALID",
                        "types must contain 'student' and/or 'team' (e.g., types=student or types=team,student).");
                }

                // Apply type filtering
                q = q.Where(c =>
                    (wantStudent && (
                        c.CertificateType == CertificateTypeConstants.Student ||
                        (c.CertificateType == null && c.StudentId != null)
                    )) ||
                    (wantTeam && (
                        c.CertificateType == CertificateTypeConstants.Team ||
                        (c.CertificateType == null && c.TeamId != null)
                    ))
                );
            }



            // Apply sorting
            q = (sortBy?.ToLowerInvariant()) switch
            {
                "issuedat" => desc ? q.OrderByDescending(c => c.IssuedAt) : q.OrderBy(c => c.IssuedAt),
                _ => desc ? q.OrderByDescending(c => c.IssuedAt) : q.OrderBy(c => c.IssuedAt)
            };

            // Get paginated result
            PaginatedList<Certificate> pageData = await repo.GetPagingAsync(q, page, pageSize);

            // Map to DTOs
            List<CertificateDTO> items = pageData.Items.Select(c => new CertificateDTO
            {
                CertificateId = c.CertificateId,
                TemplateId = c.TemplateId,
                TemplateName = c.Template.Name,
                ContestId = c.Template.ContestId,
                TeamId = c.TeamId,
                TeamName = c.Team?.Name,
                StudentId = c.StudentId,
                StudentName = c.Student?.User?.Fullname,
                CertificateType = c.CertificateType ?? (c.StudentId != null ? CertificateTypeConstants.Student : CertificateTypeConstants.Team),
                FileUrl = c.FileUrl,
                IssuedAt = c.IssuedAt
            }).ToList();

            // Return paginated DTOs
            return new PaginatedList<CertificateDTO>(items, pageData.TotalCount, pageData.PageNumber, pageData.PageSize);
        }
        public async Task<CertificateDTO> UpdateAsync(Guid certificateId, UpdateCertificateDTO dto)
        {
            if (certificateId == Guid.Empty)
                throw new ErrorException(StatusCodes.Status400BadRequest, "BAD_REQUEST", "CertificateId is invalid.");

            if (dto == null)
                throw new ErrorException(StatusCodes.Status400BadRequest, "BAD_REQUEST", "Payload cannot be null.");

            var repo = _unitOfWork.GetRepository<Certificate>();

            var cert = await repo.Entities
                .Include(x => x.Template).ThenInclude(t => t.Contest)
                .Include(x => x.Team)
                .Include(x => x.Student).ThenInclude(s => s.User)
                .FirstOrDefaultAsync(x => x.CertificateId == certificateId && x.DeletedAt == null);

            if (cert == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "CERT_NOT_FOUND", "Certificate not found.");

            await EnsureContestOwnedByOrganizerOrAdminAsync(cert.Template.ContestId);

            if (!string.IsNullOrWhiteSpace(dto.FileUrl))
                cert.FileUrl = dto.FileUrl;

            if (dto.IssuedAt.HasValue)
                cert.IssuedAt = dto.IssuedAt.Value;

            repo.Update(cert);
            await _unitOfWork.SaveAsync();

            return new CertificateDTO
            {
                CertificateId = cert.CertificateId,
                TemplateId = cert.TemplateId,
                TemplateName = cert.Template.Name,
                ContestId = cert.Template.ContestId,
                TeamId = cert.TeamId,
                TeamName = cert.Team?.Name,
                StudentId = cert.StudentId,
                StudentName = cert.Student?.User?.Fullname,
                FileUrl = cert.FileUrl,
                IssuedAt = cert.IssuedAt
            };
        }

        public async Task SoftDeleteAsync(Guid certificateId)
        {
            if (certificateId == Guid.Empty)
                throw new ErrorException(StatusCodes.Status400BadRequest, "BAD_REQUEST", "CertificateId is invalid.");

            var repo = _unitOfWork.GetRepository<Certificate>();

            var cert = await repo.Entities
                .Include(x => x.Template).ThenInclude(t => t.Contest)
                .FirstOrDefaultAsync(x => x.CertificateId == certificateId && x.DeletedAt == null);

            if (cert == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "CERT_NOT_FOUND", "Certificate not found.");

            await EnsureContestOwnedByOrganizerOrAdminAsync(cert.Template.ContestId);

            cert.DeletedAt = DateTime.UtcNow;
            repo.Update(cert);
            await _unitOfWork.SaveAsync();
        }

        // Helper to build a unique key for recipient
        private static string BuildRecipientKey(Guid? studentId, Guid? teamId)
        {
            if (studentId.HasValue) return $"S:{studentId.Value}";
            if (teamId.HasValue) return $"T:{teamId.Value}";
            return "INVALID";
        }

        private Guid? TryGetCurrentUserId()
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(userId, out var guid) ? guid : null;
        }

        private Guid GetCurrentUserIdOrThrow()
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userId, out var guid)) return guid;

            throw new ErrorException(StatusCodes.Status401Unauthorized,
                ResponseCodeConstants.UNAUTHORIZED,
                "User not authenticated.");
        }

        private bool IsAdmin()
        {
            var role = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role);
            return string.Equals(role, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase);
        }

        private async Task EnsureContestOwnedByOrganizerOrAdminAsync(Guid contestId)
        {
            var contestRepo = _unitOfWork.GetRepository<Contest>();
            var contest = await contestRepo.Entities
                .Where(c => c.ContestId == contestId && c.DeletedAt == null)
                .FirstOrDefaultAsync();

            if (contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "CONTEST_NOT_FOUND", $"No contest with ID={contestId}");

            if (IsAdmin())
                return;

            var userId = GetCurrentUserIdOrThrow();
            if (!string.Equals(contest.CreatedBy, userId.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(StatusCodes.Status403Forbidden,
                    ResponseCodeConstants.FORBIDDEN,
                    "Only the organizer who created this contest can manage certificates.");
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

    }
}
