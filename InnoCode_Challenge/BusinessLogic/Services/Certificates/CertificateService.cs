using AutoMapper;
using BusinessLogic.IServices.Certificates;
using BusinessLogic.IServices.FileStorages;
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

        public CertificateService(IMapper mapper, IUOW unitOfWork, ICloudinaryService cloud, IHttpClientFactory httpClientFactory, ILogger<CertificateService> logger, IHttpContextAccessor httpContextAccessor)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
            _cloud = cloud;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
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

                // Fetch the template
                _logger.LogDebug("Fetching certificate template with TemplateId={TemplateId}", dto.TemplateId);
                CertificateTemplate? tpl = await tplRepo.Entities.Include(t => t.Contest).FirstOrDefaultAsync(t => t.TemplateId == dto.TemplateId && t.DeletedAt == null);

                if (tpl == null)
                {
                    _logger.LogWarning("Certificate template not found. TemplateId={TemplateId}", dto.TemplateId);
                    throw new ErrorException(StatusCodes.Status404NotFound, CertificateErrorCodeConstants.TemplateNotFound, $"No template with ID={dto.TemplateId}");
                }

                _logger.LogDebug("Template found: {TemplateName}, FileUrl={FileUrl}", tpl.Name, tpl.FileUrl);

                // Download the template file
                try
                {
                    _logger.LogDebug("Downloading template file from {FileUrl}", tpl.FileUrl);
                    HttpClient? http = _httpClientFactory.CreateClient();
                    var bytes = await http.GetByteArrayAsync(tpl.FileUrl!);
                    using MemoryStream templateStream = new MemoryStream(bytes);

                    _logger.LogInformation("Template file downloaded successfully. Size={FileSize} bytes", bytes.Length);

                    // Prepare results list
                    List<IssuedCertificateDTO>? results = new List<IssuedCertificateDTO>();

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
                                    c.TeamId == teamId &&
                                    c.DeletedAt == null);
                                if (exists)
                                {
                                    _logger.LogWarning("Duplicate certificate detected. TemplateId={TemplateId}, StudentId={StudentId}, TeamId={TeamId}", 
                                        tpl.TemplateId, studentId, teamId);
                                    throw new ErrorException(StatusCodes.Status409Conflict, CertificateErrorCodeConstants.DuplicateCertificate, "Certificate already exists for this recipient and template.");
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
                            Certificate entity;
                            if (dto.Reissue)
                            {
                                _logger.LogDebug("Reissue mode: checking for existing certificate");
                                // Fetch existing certificate
                                entity = await certRepo.Entities.FirstOrDefaultAsync(c =>
                                    c.TemplateId == tpl.TemplateId && c.StudentId == studentId && c.TeamId == teamId && c.DeletedAt == null);

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
                                FileUrl = entity.FileUrl,
                                IssuedAt = entity.IssuedAt
                            });

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
                FileUrl = certificate.FileUrl,
                IssuedAt = certificate.IssuedAt
            };
        }

        public async Task<PaginatedList<CertificateDTO>> GetAsync(
            Guid? contestId,
            Guid? templateId,
            Guid? teamId,
            Guid? studentId,
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
                // Get current user ID from HTTP context
                string? userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

                // If user ID is not found, throw an unauthorized error
                if (string.IsNullOrWhiteSpace(userId))
                {
                    throw new ErrorException(StatusCodes.Status401Unauthorized,
                        ResponseCodeConstants.UNAUTHORIZED,
                        "User ID not found.");
                }

                // Parse user ID to GUID
                Guid.TryParse(userId, out Guid userGuid);

                // Get the current student's ID
                IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();

                // Find the student associated with the current user
                Guid? currentStudentId = await studentRepo
                    .Entities
                    .Where(s => s.UserId == userGuid && s.DeletedAt == null)
                    .Select(s => s.StudentId)
                    .FirstOrDefaultAsync();

                // Get certificates for the current logged-in student
                q = q.Where(c => c.StudentId == currentStudentId);
            }

            // Apply filters
            if (templateId.HasValue) q = q.Where(c => c.TemplateId == templateId.Value);
            if (contestId.HasValue) q = q.Where(c => c.Template.ContestId == contestId.Value);
            if (teamId.HasValue) q = q.Where(c => c.TeamId == teamId.Value);
            if (studentId.HasValue) q = q.Where(c => c.StudentId == studentId.Value);

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
                .FirstOrDefaultAsync(x => x.CertificateId == certificateId && x.DeletedAt == null);

            if (cert == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "CERT_NOT_FOUND", "Certificate not found.");

            cert.DeletedAt = DateTime.UtcNow;
            repo.Update(cert);
            await _unitOfWork.SaveAsync();
        }

    }
}
