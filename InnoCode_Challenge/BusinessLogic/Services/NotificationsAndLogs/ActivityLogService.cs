using AutoMapper;
using BusinessLogic.IServices.NotificationsAndLogs;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.ActivityLogDTOs;
using Repository.IRepositories;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.NotificationsAndLogs
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

            // batch load users for this page
            var userIds = page.Items.Select(x => x.UserId).Distinct().ToList();
            var userRepo = _uow.GetRepository<User>();

            var users = await userRepo.Entities.AsNoTracking()
                .Where(u => u.DeletedAt == null && userIds.Contains(u.UserId))
                .Select(u => new { u.UserId, u.Fullname, u.Email })
                .ToDictionaryAsync(x => x.UserId);

            var items = page.Items.Select(x =>
            {
                var dto = _mapper.Map<ActivityLogDTO>(x);
                if (users.TryGetValue(x.UserId, out var u))
                {
                    dto.UserFullname = u.Fullname;
                    dto.UserEmail = u.Email;
                }
                return dto;
            }).ToList();

            return new PaginatedList<ActivityLogDTO>(items, page.TotalCount, page.PageNumber, page.PageSize);
        }

        public async Task<ActivityLogDTO> GetByIdAsync(Guid id)
        {
            var repo = _uow.GetRepository<ActivityLog>();
            var entity = await repo.Entities.AsNoTracking()
                .FirstOrDefaultAsync(l => l.LogId == id && l.DeletedAt == null);

            if (entity == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "LOG_NOT_FOUND", $"No log with ID={id}");

            var dto = _mapper.Map<ActivityLogDTO>(entity);

            // attach user display
            var userRepo = _uow.GetRepository<User>();
            var u = await userRepo.Entities.AsNoTracking()
                .Where(x => x.DeletedAt == null && x.UserId == entity.UserId)
                .Select(x => new { x.Fullname, x.Email })
                .FirstOrDefaultAsync();

            dto.UserFullname = u?.Fullname;
            dto.UserEmail = u?.Email;

            return dto;
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
