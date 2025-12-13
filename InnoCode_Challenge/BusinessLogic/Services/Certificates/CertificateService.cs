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
            // Get repositories
            IGenericRepository<CertificateTemplate> tplRepo = _unitOfWork.GetRepository<CertificateTemplate>();
            IGenericRepository<Certificate> certRepo = _unitOfWork.GetRepository<Certificate>();
            IGenericRepository<Student> studentRepo = _unitOfWork.GetRepository<Student>();
            IGenericRepository<Team> teamRepo = _unitOfWork.GetRepository<Team>();

            // Fetch the template
            CertificateTemplate? tpl = await tplRepo.Entities.Include(t => t.Contest).FirstOrDefaultAsync(t => t.TemplateId == dto.TemplateId);

            // Validate template existence
            if (tpl == null)
                throw new ErrorException(StatusCodes.Status404NotFound, CertificateErrorCodeConstants.TemplateNotFound, $"No template with ID={dto.TemplateId}");

            // Download the template file
            HttpClient? http = _httpClientFactory.CreateClient();
            var bytes = await http.GetByteArrayAsync(tpl.FileUrl!);
            using MemoryStream templateStream = new MemoryStream(bytes);

            // Prepare results list
            List<IssuedCertificateDTO>? results = new List<IssuedCertificateDTO>();

            // Issue certificates to each recipient
            foreach (IssueRecipientDTO r in dto.Recipients)
            {
                bool isStudent = r.StudentId.HasValue;
                bool isTeam = r.TeamId.HasValue;

                // Validate recipient specification
                if (isStudent == isTeam)
                    throw new ErrorException(StatusCodes.Status400BadRequest, "RECIPIENT_INVALID",
                        "Specify exactly one of studentId or teamId.");

                // Fetch recipient details
                string recipientName;
                Guid? studentId = null; Guid? teamId = null;

                // Validate and get student or team
                if (isStudent)
                {
                    // Fetch student
                    Student? stu = await studentRepo.Entities.Include(s => s.User)
                        .FirstOrDefaultAsync(s => s.StudentId == r.StudentId && s.DeletedAt == null);

                    // Validate student existence
                    if (stu == null)
                        throw new ErrorException(StatusCodes.Status404NotFound,
                            CertificateErrorCodeConstants.StudentNotFound,
                            $"No student with ID={r.StudentId}");
                    studentId = stu.StudentId;
                    recipientName = r.DisplayName?.Trim() ?? (stu.User?.Fullname ?? "Student");
                }
                else
                {
                    // Fetch team
                    Team? tm = await teamRepo.Entities.FirstOrDefaultAsync(t => t.TeamId == r.TeamId && t.DeletedAt == null);

                    // Validate team existence
                    if (tm == null)
                        throw new ErrorException(StatusCodes.Status404NotFound,
                            CertificateErrorCodeConstants.TeamNotFound,
                            $"No team with ID={r.TeamId}");

                    // Set team details
                    teamId = tm.TeamId;
                    recipientName = r.DisplayName?.Trim() ?? tm.Name;
                }

                // Check for duplicate certificate if not reissuing
                if (!dto.Reissue)
                {
                    bool exists = await certRepo.Entities.AnyAsync(c =>
                        c.TemplateId == tpl.TemplateId &&
                        c.StudentId == studentId &&
                        c.TeamId == teamId &&
                        c.DeletedAt == null);
                    if (exists)
                        throw new ErrorException(StatusCodes.Status409Conflict, CertificateErrorCodeConstants.DuplicateCertificate, "Certificate already exists for this recipient and template.");
                }

                // Render text on the certificate template
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
                var pngBytes = ImageDrawHelper.RenderTextOnImage(
                    template: templateStream,
                    displayText: recipientName,
                    fontFamily: layout.FontFamily,
                    fontSize: layout.FontSize,
                    colorHex: layout.ColorHex,
                    x: layout.X, y: layout.Y, maxWidth: layout.MaxWidth, align: layout.Align);
                templateStream.Position = 0;

                // Upload the rendered certificate to cloud storage
                string fileName = $"cert_{tpl.TemplateId}_{studentId?.ToString() ?? teamId!.ToString()}_{DateTime.UtcNow:yyyyMMddHHmmss}.png";

                // Upload to cloud
                string url;
                try
                {
                    using var mem = new MemoryStream(pngBytes);
                    url = await _cloud.UploadImageAsync(mem, "certificates", fileName);
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
                    // Fetch existing certificate
                    entity = await certRepo.Entities.FirstOrDefaultAsync(c =>
                        c.TemplateId == tpl.TemplateId && c.StudentId == studentId && c.TeamId == teamId && c.DeletedAt == null);

                    // If not found, create new
                    if (entity == null)
                    {
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
                        entity.FileUrl = url;
                        entity.IssuedAt = DateTime.UtcNow;
                        certRepo.Update(entity);
                    }
                }
                // New issuance
                else
                {
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
                await _unitOfWork.SaveAsync();

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

            return results;

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
        private static IFormFile ToFormFile(byte[] data, string fileName, string contentType)
        {
            var stream = new MemoryStream(data); 
            return new FormFile(stream, 0, data.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
        }

    }
}
