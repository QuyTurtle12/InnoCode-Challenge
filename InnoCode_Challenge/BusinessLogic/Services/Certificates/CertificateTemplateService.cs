using AutoMapper;
using BusinessLogic.IServices.Certificates;
using BusinessLogic.IServices.FileStorages;
using BusinessLogic.IServices.NotificationsAndLogs;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Utility.Constant;
using Repository.DTOs.CertificateTemplateDTOs;
using Repository.IRepositories;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Certificates
{
    public class CertificateTemplateService : ICertificateTemplateService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IActivityLogWriter _logWriter;

        private const long MAX_FILE_SIZE = 10 * 1024 * 1024;

        // Constructor
        public CertificateTemplateService(
            IMapper mapper,
            IUOW unitOfWork,
            ICloudinaryService cloudinaryService,
            IHttpContextAccessor httpContextAccessor,
            IActivityLogWriter logWriter)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
            _cloudinaryService = cloudinaryService;
            _httpContextAccessor = httpContextAccessor;
            _logWriter = logWriter;
        }

        public async Task<CertificateTemplateDTO> CreateAsync(CreateCertificateTemplateDTO dto)
        {
            IGenericRepository<CertificateTemplate> repo = _unitOfWork.GetRepository<CertificateTemplate>();
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

            await EnsureContestOwnedByOrganizerOrAdminAsync(dto.ContestId, contestRepo);

            var entity = new CertificateTemplate
            {
                TemplateId = Guid.NewGuid(),
                ContestId = dto.ContestId,
                Name = dto.Name.Trim(),
                FileUrl = dto.FileUrl,
                TextX = dto.Text?.X,    
                TextY = dto.Text?.Y,
                DeletedAt = null
            };

            await repo.InsertAsync(entity);
            await _unitOfWork.SaveAsync();

            var actorId = GetCurrentUserIdOrThrow();
            await _logWriter.TryWriteAsync(
                actorId,
                ActivityActions.CertTemplateCreate,
                TargetTypes.CertificateTemplate,
                entity.TemplateId.ToString());

            return new CertificateTemplateDTO
            {
                TemplateId = entity.TemplateId,
                ContestId = entity.ContestId,
                Name = entity.Name,
                FileUrl = entity.FileUrl,
                Text = BuildTextLayout(entity.TextX, entity.TextY)
            };
        }

        public async Task<CertificateTemplateDTO?> GetByIdAsync(Guid id)
        {
            var repo = _unitOfWork.GetRepository<CertificateTemplate>();

            var tpl = await repo.Entities
                .Where(t => t.TemplateId == id && t.DeletedAt == null)
                .Include(t => t.Contest)
                .FirstOrDefaultAsync();

            if (tpl == null) return null;

            await EnsureContestOwnedByOrganizerOrAdminAsync(tpl.ContestId, _unitOfWork.GetRepository<Contest>());

            return new CertificateTemplateDTO
            {
                TemplateId = tpl.TemplateId,
                ContestId = tpl.ContestId,
                Name = tpl.Name,
                FileUrl = tpl.FileUrl,
                Text = BuildTextLayout(tpl.TextX, tpl.TextY)
            };
        }

        public async Task<PaginatedList<CertificateTemplateDTO>> GetAsync(Guid? contestId, string? search, int page, int pageSize)
        {
            var repo = _unitOfWork.GetRepository<CertificateTemplate>();
            var contestRepo = _unitOfWork.GetRepository<Contest>();

            IQueryable<CertificateTemplate> query = repo.Entities
                .AsNoTracking()
                .Where(x => x.DeletedAt == null)
                .Include(x => x.Contest);

            if (contestId.HasValue)
            {
                await EnsureContestOwnedByOrganizerOrAdminAsync(contestId.Value, contestRepo);
                query = query.Where(x => x.ContestId == contestId.Value);
            }
            else if (!IsAdmin())
            {
                var userId = GetCurrentUserIdOrThrow();
                query = query.Where(x => x.Contest.CreatedBy == userId.ToString());
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.Trim().ToLower();
                query = query.Where(x => x.Name.ToLower().Contains(s));
            }

            query = query.OrderByDescending(x => x.TemplateId);

            var pageData = await repo.GetPagingAsync(query, page, pageSize);

            var items = pageData.Items.Select(t => new CertificateTemplateDTO
            {
                TemplateId = t.TemplateId,
                ContestId = t.ContestId,
                Name = t.Name,
                FileUrl = t.FileUrl,
                Text = BuildTextLayout(t.TextX, t.TextY)
            }).ToList();

            return new PaginatedList<CertificateTemplateDTO>(items, pageData.TotalCount, pageData.PageNumber, pageData.PageSize);
        }

        public async Task<CertificateTemplateDTO> UpdateAsync(Guid templateId, UpdateCertificateTemplateDTO dto)
        {
            if (templateId == Guid.Empty)
                throw new ErrorException(StatusCodes.Status400BadRequest, "BAD_REQUEST", "TemplateId is invalid.");

            if (dto == null)
                throw new ErrorException(StatusCodes.Status400BadRequest, "BAD_REQUEST", "Payload cannot be null.");

            var repo = _unitOfWork.GetRepository<CertificateTemplate>();

            var tpl = await repo.Entities
                .Include(t => t.Contest)
                .FirstOrDefaultAsync(t => t.TemplateId == templateId && t.DeletedAt == null);

            if (tpl == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "TEMPLATE_NOT_FOUND", "Template not found.");

            await EnsureContestOwnedByOrganizerOrAdminAsync(tpl.ContestId, _unitOfWork.GetRepository<Contest>());

            // Optional updates
            if (!string.IsNullOrWhiteSpace(dto.Name))
                tpl.Name = dto.Name.Trim();

            if (!string.IsNullOrWhiteSpace(dto.FileUrl))
                tpl.FileUrl = dto.FileUrl.Trim();

            if (dto.Text != null)
            {
                if (dto.Text.X.HasValue) tpl.TextX = dto.Text.X.Value;
                if (dto.Text.Y.HasValue) tpl.TextY = dto.Text.Y.Value;
            }

            repo.Update(tpl);
            await _unitOfWork.SaveAsync();

            var actorId = GetCurrentUserIdOrThrow();
            await _logWriter.TryWriteAsync(
                actorId,
                ActivityActions.CertTemplateUpdate,
                TargetTypes.CertificateTemplate,
                tpl.TemplateId.ToString());

            return new CertificateTemplateDTO
            {
                TemplateId = tpl.TemplateId,
                ContestId = tpl.ContestId,
                Name = tpl.Name,
                FileUrl = tpl.FileUrl,
                Text = BuildTextLayout(tpl.TextX, tpl.TextY)
            };
        }

        public async Task SoftDeleteAsync(Guid templateId)
        {
            if (templateId == Guid.Empty)
                throw new ErrorException(StatusCodes.Status400BadRequest, "BAD_REQUEST", "TemplateId is invalid.");

            var repo = _unitOfWork.GetRepository<CertificateTemplate>();

            var tpl = await repo.Entities
                .Include(t => t.Contest)
                .FirstOrDefaultAsync(t => t.TemplateId == templateId && t.DeletedAt == null);

            if (tpl == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "TEMPLATE_NOT_FOUND", "Template not found.");

            await EnsureContestOwnedByOrganizerOrAdminAsync(tpl.ContestId, _unitOfWork.GetRepository<Contest>());

            tpl.DeletedAt = DateTime.UtcNow;
            repo.Update(tpl);
            await _unitOfWork.SaveAsync();

            var actorId = GetCurrentUserIdOrThrow();
            await _logWriter.TryWriteAsync(
                actorId,
                ActivityActions.CertTemplateDelete,
                TargetTypes.CertificateTemplate,
                tpl.TemplateId.ToString());
        }

        private TextLayoutDTO BuildTextLayout(decimal? x, decimal? y)
        {
            return new TextLayoutDTO
            {
                X = (int)(x ?? CertificateTemplateDefaults.TextX),
                Y = (int)(y ?? CertificateTemplateDefaults.TextY),
                FontFamily = CertificateTemplateDefaults.FontFamily,
                FontSize = CertificateTemplateDefaults.FontSize,
                ColorHex = CertificateTemplateDefaults.ColorHex,
                MaxWidth = CertificateTemplateDefaults.MaxWidth,
                Align = CertificateTemplateDefaults.Align
            };
        }

        private Guid GetCurrentUserIdOrThrow()
        {
            var str = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(str, out var g)) return g;
            throw new ErrorException(StatusCodes.Status401Unauthorized, ResponseCodeConstants.UNAUTHORIZED, "User not authenticated.");
        }

        private bool IsAdmin()
        {
            var role = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role);
            return string.Equals(role, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase);
        }

        private async Task EnsureContestOwnedByOrganizerOrAdminAsync(Guid contestId, IGenericRepository<Contest> contestRepo)
        {
            var contest = await contestRepo.Entities
                .Where(c => c.ContestId == contestId && c.DeletedAt == null)
                .FirstOrDefaultAsync();

            if (contest == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "CONTEST_NOT_FOUND", $"No contest with ID={contestId}");

            if (IsAdmin())
                return;

            var userId = GetCurrentUserIdOrThrow();
            if (!string.Equals(contest.CreatedBy, userId.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new ErrorException(StatusCodes.Status403Forbidden, "FORBIDDEN", "Only the organizer who created this contest can manage certificate templates.");
        }


    }
}
