using AutoMapper;
using BusinessLogic.IServices.NotificationsAndLogs;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.ActivityLogDTOs;
using Repository.IRepositories;
using Utility.Constant;
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
            ValidatePaging(query.Page, query.PageSize);

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
            {
                var target = query.TargetType.Trim().ToLowerInvariant();
                q = q.Where(l => l.TargetType != null && l.TargetType.ToLower() == target);
            }

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
            var users = await LoadUserDisplaysAsync(page.Items.Select(x => x.UserId));

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
            var users = await LoadUserDisplaysAsync(new[] { entity.UserId });
            if (users.TryGetValue(entity.UserId, out var u))
            {
                dto.UserFullname = u.Fullname;
                dto.UserEmail = u.Email;
            }

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

        private static void ValidatePaging(int page, int pageSize)
        {
            if (page <= 0 || pageSize <= 0)
            {
                throw new ErrorException(
                    StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "page and pageSize must be greater than 0.");
            }
        }

        private async Task<Dictionary<Guid, (string? Fullname, string? Email)>> LoadUserDisplaysAsync(IEnumerable<Guid> userIds)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<Guid, (string?, string?)>();

            var userRepo = _uow.GetRepository<User>();
            return await userRepo.Entities.AsNoTracking()
                .Where(u => u.DeletedAt == null && ids.Contains(u.UserId))
                .Select(u => new { u.UserId, Fullname = (string?)u.Fullname, Email = (string?)u.Email })
                .ToDictionaryAsync(x => x.UserId, x => (x.Fullname, x.Email));
        }
    }
}
