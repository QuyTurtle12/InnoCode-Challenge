using AutoMapper;
using BusinessLogic.IServices.Certificates;
using BusinessLogic.IServices.FileStorages;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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

        private const long MAX_FILE_SIZE = 10 * 1024 * 1024;

        // Constructor
        public CertificateTemplateService(IMapper mapper, IUOW unitOfWork, ICloudinaryService cloudinaryService)
        {
            _mapper = mapper;
            _unitOfWork = unitOfWork;
            _cloudinaryService = cloudinaryService;
        }

        public async Task<CertificateTemplateDTO> CreateAsync(CreateCertificateTemplateDTO dto)
        {
            IGenericRepository<CertificateTemplate> repo = _unitOfWork.GetRepository<CertificateTemplate>();
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

            bool contestExists = await contestRepo.Entities
                .AnyAsync(c => c.ContestId == dto.ContestId && c.DeletedAt == null);

            if (!contestExists)
                throw new ErrorException(StatusCodes.Status404NotFound, "CONTEST_NOT_FOUND", $"No contest with ID={dto.ContestId}");

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

            return new CertificateTemplateDTO
            {
                TemplateId = entity.TemplateId,
                ContestId = entity.ContestId,
                Name = entity.Name,
                FileUrl = entity.FileUrl,
                Text = new TextLayoutDTO
                {
                    X = (int)(entity.TextX ?? 960),
                    Y = (int)(entity.TextY ?? 540),

                    FontFamily = dto.Text?.FontFamily ?? "Arial",
                    FontSize = dto.Text?.FontSize ?? 64f,
                    ColorHex = dto.Text?.ColorHex ?? "#1F2937",
                    MaxWidth = dto.Text?.MaxWidth ?? 1600,
                    Align = dto.Text?.Align ?? "center"
                },
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

            return new CertificateTemplateDTO
            {
                TemplateId = tpl.TemplateId,
                ContestId = tpl.ContestId,
                Name = tpl.Name,
                FileUrl = tpl.FileUrl,
                Text = new TextLayoutDTO
                {
                    X = (int)(tpl.TextX ?? 960),
                    Y = (int)(tpl.TextY ?? 540),
                    FontFamily = "Arial",
                    FontSize = 64f,
                    ColorHex = "#1F2937",
                    MaxWidth = 1600,
                    Align = "center"
                },
            };
        }

        public async Task<PaginatedList<CertificateTemplateDTO>> GetAsync(Guid? contestId, string? search, int page, int pageSize, string? sortBy, bool desc)
        {
            var repo = _unitOfWork.GetRepository<CertificateTemplate>();

            IQueryable<CertificateTemplate> query = repo.Entities
                .AsNoTracking()
                .Where(x => x.DeletedAt == null);

            if (contestId.HasValue) query = query.Where(x => x.ContestId == contestId.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.Trim().ToLower();
                query = query.Where(x => x.Name.ToLower().Contains(s));
            }

            query = (sortBy?.ToLowerInvariant()) switch
            {
                "name" => desc ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
                _ => desc ? query.OrderByDescending(x => x.TemplateId) : query.OrderBy(x => x.TemplateId)
            };

            var pageData = await repo.GetPagingAsync(query, page, pageSize);

            var items = pageData.Items.Select(t => new CertificateTemplateDTO
            {
                TemplateId = t.TemplateId,
                ContestId = t.ContestId,
                Name = t.Name,
                FileUrl = t.FileUrl,
                Text = new TextLayoutDTO
                {
                    X = (int)(t.TextX ?? 960),
                    Y = (int)(t.TextY ?? 540),
                    FontFamily = "Arial",
                    FontSize = 64f,
                    ColorHex = "#1F2937",
                    MaxWidth = 1600,
                    Align = "center"
                },
            }).ToList();

            return new PaginatedList<CertificateTemplateDTO>(items, pageData.TotalCount, pageData.PageNumber, pageData.PageSize);
        }

    }
}
