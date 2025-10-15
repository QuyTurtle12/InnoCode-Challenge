using AutoMapper;
using BusinessLogic.IServices;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.ActivityLogDTOs;
using Repository.IRepositories;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services
{
    public class ActivityLogService : IActivityLogService
    {
        private readonly IUOW _uow;
        private readonly IMapper _mapper;

        public ActivityLogService(IUOW uow, IMapper mapper)
        {
            _uow = uow;
            _mapper = mapper;
        }

        public async Task<PaginatedList<ActivityLogDTO>> GetAsync(ActivityLogQueryParams query)
        {
            var repo = _uow.GetRepository<ActivityLog>();
            var q = repo.Entities.Where(l => l.DeletedAt == null).AsNoTracking();

            if (query.UserId.HasValue)
                q = q.Where(l => l.UserId == query.UserId.Value);

            if (!string.IsNullOrWhiteSpace(query.ActionContains))
            {
                var k = query.ActionContains.Trim().ToLower();
                q = q.Where(l => l.Action.ToLower().Contains(k));
            }

            if (!string.IsNullOrWhiteSpace(query.TargetType))
                q = q.Where(l => l.TargetType == query.TargetType);

            if (query.From.HasValue)
                q = q.Where(l => l.At >= query.From.Value);

            if (query.To.HasValue)
                q = q.Where(l => l.At <= query.To.Value);

            q = (query.SortBy?.ToLowerInvariant()) switch
            {
                "action" => query.Desc ? q.OrderByDescending(l => l.Action) : q.OrderBy(l => l.Action),
                _ => query.Desc ? q.OrderByDescending(l => l.At) : q.OrderBy(l => l.At),
            };

            var page = await repo.GetPagingAsync(q, query.Page, query.PageSize);
            var items = page.Items.Select(_mapper.Map<ActivityLogDTO>).ToList();
            return new PaginatedList<ActivityLogDTO>(items, page.TotalCount, page.PageNumber, page.PageSize);
        }

        public async Task<ActivityLogDTO> GetByIdAsync(Guid id)
        {
            var repo = _uow.GetRepository<ActivityLog>();
            var entity = await repo.Entities.AsNoTracking()
                .FirstOrDefaultAsync(l => l.LogId == id && l.DeletedAt == null);

            if (entity == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "LOG_NOT_FOUND", $"No log with ID={id}");

            return _mapper.Map<ActivityLogDTO>(entity);
        }

        public async Task<ActivityLogDTO> CreateAsync(CreateActivityLogDTO dto)
        {
            var userRepo = _uow.GetRepository<User>();
            var exists = await userRepo.Entities.AnyAsync(u => u.UserId == dto.UserId && u.DeletedAt == null);
            if (!exists)
                throw new ErrorException(StatusCodes.Status404NotFound, "USER_NOT_FOUND", $"No user with ID={dto.UserId}");

            var repo = _uow.GetRepository<ActivityLog>();
            var entity = _mapper.Map<ActivityLog>(dto);
            await repo.InsertAsync(entity);
            await _uow.SaveAsync();
            return _mapper.Map<ActivityLogDTO>(entity);
        }

        public async Task DeleteAsync(Guid id)
        {
            var repo = _uow.GetRepository<ActivityLog>();
            var entity = await repo.Entities.FirstOrDefaultAsync(l => l.LogId == id && l.DeletedAt == null);
            if (entity == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "LOG_NOT_FOUND", $"No log with ID={id}");

            entity.DeletedAt = DateTime.UtcNow;
            repo.Update(entity);
            await _uow.SaveAsync();
        }
    }
}
