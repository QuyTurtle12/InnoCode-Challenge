using AutoMapper;
using BusinessLogic.IServices.Certificates;
using BusinessLogic.IServices.FileStorages;
using CloudinaryDotNet;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Repository.DTOs.CertificateDTOs;
using Repository.IRepositories;
using Repository.Repositories;
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
        private readonly ILogger<CertificateService> _logger;
        public CertificateService(IMapper mapper, IUOW unitOfWork, ICloudinaryService cloud, IHttpClientFactory httpClientFactory, ILogger<CertificateService> logger)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
            _cloud = cloud;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<IReadOnlyList<IssuedCertificateDTO>> IssueAsync(IssueCertificatesDTO dto)
        {
            var tplRepo = _unitOfWork.GetRepository<CertificateTemplate>();
            var certRepo = _unitOfWork.GetRepository<Certificate>();
            var studentRepo = _unitOfWork.GetRepository<Student>();
            var teamRepo = _unitOfWork.GetRepository<Team>();

            var tpl = await tplRepo.Entities.Include(t => t.Contest).FirstOrDefaultAsync(t => t.TemplateId == dto.TemplateId);
            if (tpl == null)
                throw new ErrorException(StatusCodes.Status404NotFound, CertificateErrorCodeConstants.TemplateNotFound, $"No template with ID={dto.TemplateId}");

            var http = _httpClientFactory.CreateClient();
            var bytes = await http.GetByteArrayAsync(tpl.FileUrl!);
            using var templateStream = new MemoryStream(bytes);

            var results = new List<IssuedCertificateDTO>();

            foreach (var r in dto.Recipients)
            {
                var isStudent = r.StudentId.HasValue;
                var isTeam = r.TeamId.HasValue;
                if (isStudent == isTeam)
                    throw new ErrorException(StatusCodes.Status400BadRequest, "RECIPIENT_INVALID",
                        "Specify exactly one of studentId or teamId.");

                string recipientName;
                Guid? studentId = null; Guid? teamId = null;

                if (isStudent)
                {
                    var stu = await studentRepo.Entities.Include(s => s.User)
                        .FirstOrDefaultAsync(s => s.StudentId == r.StudentId && s.DeletedAt == null);
                    if (stu == null)
                        throw new ErrorException(StatusCodes.Status404NotFound,
                            CertificateErrorCodeConstants.StudentNotFound,
                            $"No student with ID={r.StudentId}");
                    studentId = stu.StudentId;
                    recipientName = r.DisplayName?.Trim() ?? (stu.User?.Fullname ?? "Student");
                }
                else
                {
                    var tm = await teamRepo.Entities.FirstOrDefaultAsync(t => t.TeamId == r.TeamId && t.DeletedAt == null);
                    if (tm == null)
                        throw new ErrorException(StatusCodes.Status404NotFound,
                            CertificateErrorCodeConstants.TeamNotFound,
                            $"No team with ID={r.TeamId}");
                    teamId = tm.TeamId;
                    recipientName = r.DisplayName?.Trim() ?? tm.Name;
                }

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

                var layout = new Repository.DTOs.CertificateTemplateDTOs.TextLayoutDTO();
                var pngBytes = ImageDrawHelper.RenderTextOnImage(
                    template: templateStream,
                    displayText: recipientName,
                    fontFamily: layout.FontFamily,
                    fontSize: layout.FontSize,
                    colorHex: layout.ColorHex,
                    x: layout.X, y: layout.Y, maxWidth: layout.MaxWidth, align: layout.Align);
                templateStream.Position = 0;

                var fileName = $"cert_{tpl.TemplateId}_{studentId?.ToString() ?? teamId!.ToString()}_{DateTime.UtcNow:yyyyMMddHHmmss}.png";

                string url;
                try
                {
                    using var mem = new MemoryStream(pngBytes);
                    url = await _cloud.UploadImageAsync(mem, "certificates", fileName); // 🔸 use image upload
                }
                catch (ErrorException ex)
                {
                    _logger.LogError(ex, "Cloudinary upload failed for {FileName}", fileName); // 🔸 log provider error
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



                Certificate entity;
                if (dto.Reissue)
                {
                    entity = await certRepo.Entities.FirstOrDefaultAsync(c =>
                        c.TemplateId == tpl.TemplateId && c.StudentId == studentId && c.TeamId == teamId && c.DeletedAt == null);

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
                    else
                    {
                        entity.FileUrl = url;
                        entity.IssuedAt = DateTime.UtcNow;
                        certRepo.Update(entity);
                    }
                }
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

                await _unitOfWork.SaveAsync();

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

        public async Task<CertificateDTO> GetByIdAsync(Guid id)
        {
            var repo = _unitOfWork.GetRepository<Certificate>();
            var c = await repo.Entities
                .Include(x => x.Template).ThenInclude(t => t.Contest)
                .Include(x => x.Team)
                .Include(x => x.Student).ThenInclude(s => s.User)
                .FirstOrDefaultAsync(x => x.CertificateId == id && x.DeletedAt == null);
            if (c == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "CERTIFICATE_NOT_FOUND", $"No certificate with ID={id}");

            return new CertificateDTO
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
            };
        }

        public async Task<PaginatedList<CertificateDTO>> GetAsync(Guid? contestId, Guid? templateId, Guid? teamId, Guid? studentId, int page, int pageSize, string? sortBy, bool desc)
        {
            var repo = _unitOfWork.GetRepository<Certificate>();
            var q = repo.Entities
                .Where(c => c.DeletedAt == null)
                .Include(c => c.Template).ThenInclude(t => t.Contest)
                .Include(c => c.Team)
                .Include(c => c.Student).ThenInclude(s => s.User)
                .AsNoTracking();

            if (templateId.HasValue) q = q.Where(c => c.TemplateId == templateId.Value);
            if (contestId.HasValue) q = q.Where(c => c.Template.ContestId == contestId.Value);
            if (teamId.HasValue) q = q.Where(c => c.TeamId == teamId.Value);
            if (studentId.HasValue) q = q.Where(c => c.StudentId == studentId.Value);

            q = (sortBy?.ToLowerInvariant()) switch
            {
                "issuedat" => desc ? q.OrderByDescending(c => c.IssuedAt) : q.OrderBy(c => c.IssuedAt),
                _ => desc ? q.OrderByDescending(c => c.IssuedAt) : q.OrderBy(c => c.IssuedAt)
            };

            var pageData = await repo.GetPagingAsync(q, page, pageSize);
            var items = pageData.Items.Select(c => new CertificateDTO
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
