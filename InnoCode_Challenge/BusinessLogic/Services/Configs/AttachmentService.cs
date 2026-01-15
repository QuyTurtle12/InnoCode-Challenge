using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.AttachmentDTOs;
using Repository.IRepositories;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services
{
    public class AttachmentService : IAttachmentService
    {
        private readonly IUOW _uow;
        private readonly IMapper _mapper;

        public AttachmentService(IUOW uow, IMapper mapper)
        {
            _uow = uow;
            _mapper = mapper;
        }

        public async Task<PaginatedList<AttachmentDTO>> GetAsync(AttachmentQueryParams query)
        {
            var repo = _uow.GetRepository<Attachment>();
            var q = repo.Entities.Where(a => a.DeletedAt == null).AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var k = query.Search.Trim().ToLower();
                q = q.Where(a => a.Url.ToLower().Contains(k));
            }

            if (!string.IsNullOrWhiteSpace(query.Type))
                q = q.Where(a => a.Type == query.Type);

            if (query.CreatedFrom.HasValue)
                q = q.Where(a => a.CreatedAt >= query.CreatedFrom.Value);

            if (query.CreatedTo.HasValue)
                q = q.Where(a => a.CreatedAt <= query.CreatedTo.Value);

            q = (query.SortBy?.ToLowerInvariant()) switch
            {
                "type" => query.Desc ? q.OrderByDescending(a => a.Type) : q.OrderBy(a => a.Type),
                "url" => query.Desc ? q.OrderByDescending(a => a.Url) : q.OrderBy(a => a.Url),
                _ => query.Desc ? q.OrderByDescending(a => a.CreatedAt) : q.OrderBy(a => a.CreatedAt),
            };

            var page = await repo.GetPagingAsync(q, query.Page, query.PageSize);
            var items = page.Items.Select(_mapper.Map<AttachmentDTO>).ToList();
            return new PaginatedList<AttachmentDTO>(items, page.TotalCount, page.PageNumber, page.PageSize);
        }

        public async Task<AttachmentDTO> GetByIdAsync(Guid id)
        {
            var repo = _uow.GetRepository<Attachment>();
            var entity = await repo.Entities.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AttachmentId == id && a.DeletedAt == null);

            if (entity == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "ATTACHMENT_NOT_FOUND", $"No attachment with ID={id}");

            return _mapper.Map<AttachmentDTO>(entity);
        }

        public async Task<AttachmentDTO> CreateAsync(CreateAttachmentDTO dto)
        {
            var repo = _uow.GetRepository<Attachment>();
            var entity = _mapper.Map<Attachment>(dto);
            await repo.InsertAsync(entity);
            await _uow.SaveAsync();
            return _mapper.Map<AttachmentDTO>(entity);
        }

        public async Task<AttachmentDTO> UpdateAsync(Guid id, UpdateAttachmentDTO dto)
        {
            var repo = _uow.GetRepository<Attachment>();
            var entity = await repo.Entities.FirstOrDefaultAsync(a => a.AttachmentId == id && a.DeletedAt == null);
            if (entity == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "ATTACHMENT_NOT_FOUND", $"No attachment with ID={id}");

            _mapper.Map(dto, entity);
            repo.Update(entity);
            await _uow.SaveAsync();
            return _mapper.Map<AttachmentDTO>(entity);
        }

        public async Task DeleteAsync(Guid id)
        {
            var repo = _uow.GetRepository<Attachment>();
            var entity = await repo.Entities.FirstOrDefaultAsync(a => a.AttachmentId == id && a.DeletedAt == null);
            if (entity == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "ATTACHMENT_NOT_FOUND", $"No attachment with ID={id}");

            entity.DeletedAt = DateTime.UtcNow;
            repo.Update(entity);
            await _uow.SaveAsync();
        }
    }
}
