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
            // Get Repositories
            IGenericRepository<CertificateTemplate> repo = _unitOfWork.GetRepository<CertificateTemplate>();
            IGenericRepository<Contest> contestRepo = _unitOfWork.GetRepository<Contest>();

            // Validate Contest Existence
            bool contestExists = await contestRepo.Entities.AnyAsync(c => c.ContestId == dto.ContestId);
            if (!contestExists)
                throw new ErrorException(StatusCodes.Status404NotFound, "CONTEST_NOT_FOUND", $"No contest with ID={dto.ContestId}");

            // Create Entity
            CertificateTemplate entity = new CertificateTemplate
            {
                TemplateId = Guid.NewGuid(),
                ContestId = dto.ContestId,
                Name = dto.Name.Trim(),
                FileUrl = dto.FileUrl
            };

            // Insert and Save
            await repo.InsertAsync(entity);
            await _unitOfWork.SaveAsync();

            // Retrieve Created Entity with Contest
            CertificateTemplate created = await repo.Entities
                .Where(t => t.TemplateId == entity.TemplateId)
                .Include(t => t.Contest)
                .FirstAsync();

            // Map to DTO
            CertificateTemplateDTO mapped = _mapper.Map<CertificateTemplateDTO>(entity);
            mapped.Text = dto.Text; 
            return mapped;
        }

        public async Task<CertificateTemplateDTO?> GetByIdAsync(Guid id)
        {
            // Get repository
            IGenericRepository<CertificateTemplate> repo = _unitOfWork.GetRepository<CertificateTemplate>();

            // Find entity
            CertificateTemplate? certificateTemplate = await repo
                .Entities
                .Where(t => t.TemplateId == id)
                .Include(t => t.Contest)
                .FirstOrDefaultAsync();

            // If not found, return null
            if (certificateTemplate == null)
            {
                return null;
            }

            // Map to DTO and return
            CertificateTemplateDTO dto = _mapper.Map<CertificateTemplateDTO>(certificateTemplate);
            return dto;
        }

        public async Task<PaginatedList<CertificateTemplateDTO>> GetAsync(Guid? contestId, string? search, int page, int pageSize, string? sortBy, bool desc)
        {
            // Get repository
            IGenericRepository<CertificateTemplate> repo = _unitOfWork.GetRepository<CertificateTemplate>();

            // Build query
            IQueryable<CertificateTemplate> query = repo.Entities.AsNoTracking();

            // Apply filters by contestId
            if (contestId.HasValue) query = query.Where(x => x.ContestId == contestId.Value);

            // Apply filters by template name
            if (!string.IsNullOrWhiteSpace(search))
            {
                string formattedSearchValue = search.Trim().ToLower();
                query = query.Where(x => x.Name.ToLower().Contains(formattedSearchValue));
            }

            // Apply sorting
            query = (sortBy?.ToLowerInvariant()) switch
            {
                "name" => desc ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
                _ => desc ? query.OrderByDescending(x => x.TemplateId) : query.OrderBy(x => x.TemplateId)
            };

            // Get paginated data
            PaginatedList<CertificateTemplate> pageData = await repo.GetPagingAsync(query, page, pageSize);

            // Map to DTOs
            IReadOnlyCollection<CertificateTemplateDTO> items = pageData.Items.Select(_mapper.Map<CertificateTemplateDTO>).ToList();

            // Return paginated list of DTOs
            return new PaginatedList<CertificateTemplateDTO>(items, pageData.TotalCount, pageData.PageNumber, pageData.PageSize);
        }
    }
}
