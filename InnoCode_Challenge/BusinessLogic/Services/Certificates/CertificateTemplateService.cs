using AutoMapper;
using BusinessLogic.IServices.Certificates;
using BusinessLogic.IServices.FileStorages;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.CertificateTemplateDTOs;
using Repository.IRepositories;
using Repository.Repositories;
using Utility.Constant;
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
            var repo = _unitOfWork.GetRepository<CertificateTemplate>();
            var contestRepo = _unitOfWork.GetRepository<Contest>();

            var contestExists = await contestRepo.Entities.AnyAsync(c => c.ContestId == dto.ContestId);
            if (!contestExists)
                throw new ErrorException(StatusCodes.Status404NotFound, "CONTEST_NOT_FOUND", $"No contest with ID={dto.ContestId}");

            var entity = new CertificateTemplate
            {
                TemplateId = Guid.NewGuid(),
                ContestId = dto.ContestId,
                Name = dto.Name.Trim(),
                FileUrl = dto.FileUrl
            };

            await repo.InsertAsync(entity);
            await _unitOfWork.SaveAsync();

            var created = await repo.Entities
    .Include(t => t.Contest)
    .AsNoTracking()
    .FirstAsync(t => t.TemplateId == entity.TemplateId);


            var mapped = _mapper.Map<CertificateTemplateDTO>(entity);
            mapped.Text = dto.Text; 
            return mapped;
        }

        public async Task<CertificateTemplateDTO> GetByIdAsync(Guid id)
        {
            var repo = _unitOfWork.GetRepository<CertificateTemplate>();
            var e = await repo.Entities.Include(t => t.Contest).FirstOrDefaultAsync(t => t.TemplateId == id);
            if (e == null)
                throw new ErrorException(StatusCodes.Status404NotFound, CertificateErrorCodeConstants.TemplateNotFound, $"No template with ID={id}");

            var dto = _mapper.Map<CertificateTemplateDTO>(e);
            return dto;
        }

        public async Task<PaginatedList<CertificateTemplateDTO>> GetAsync(Guid? contestId, string? search, int page, int pageSize, string? sortBy, bool desc)
        {
            var repo = _unitOfWork.GetRepository<CertificateTemplate>();
            var query = repo.Entities.AsNoTracking();

            if (contestId.HasValue) query = query.Where(x => x.ContestId == contestId.Value);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var k = search.Trim().ToLower();
                query = query.Where(x => x.Name.ToLower().Contains(k));
            }

            query = (sortBy?.ToLowerInvariant()) switch
            {
                "name" => desc ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
                _ => desc ? query.OrderByDescending(x => x.TemplateId) : query.OrderBy(x => x.TemplateId)
            };

            var pageData = await repo.GetPagingAsync(query, page, pageSize);
            var items = pageData.Items.Select(_mapper.Map<CertificateTemplateDTO>).ToList();
            return new PaginatedList<CertificateTemplateDTO>(items, pageData.TotalCount, pageData.PageNumber, pageData.PageSize);
        }

    }
}
