using AutoMapper;
using BusinessLogic.Hubs;
using BusinessLogic.IServices.NotificationsAndLogs;
using CloudinaryDotNet;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.NotificationDTOs;
using Repository.IRepositories;
using System.Security.Claims;
using System.Text.Json;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services.NotificationsAndLogs
{
    public class NotificationService : INotificationService
    {
        private readonly IHubContext<NotificationsHub> _hub;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IUOW _unitOfWork;

        public NotificationService(IUOW uow, IHubContext<NotificationsHub> hub, IHttpContextAccessor httpContextAccessor)
        {
            _unitOfWork = uow;
            _hub = hub;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task CreateNotificationAsync(CreateGeneralNotificationDTO dto)
        {
            if (dto == null)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Payload cannot be null.");

            if (dto.recipientEmailList == null || dto.recipientEmailList.Count == 0)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Recipient list cannot be empty.");

            var emails = dto.recipientEmailList
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();

            if (emails.Count == 0)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Recipient list cannot be empty.");

            try
            {
                _unitOfWork.BeginTransaction();

                var userRepo = _unitOfWork.GetRepository<User>();
                var notifRepo = _unitOfWork.GetRepository<Notification>();

                // Find all users by emails
                var users = await userRepo.Entities
                    .Where(u => u.DeletedAt == null && emails.Contains(u.Email.ToLower()))
                    .Select(u => new { u.UserId, u.Email })
                    .ToListAsync();

                var foundEmails = users.Select(x => x.Email.Trim().ToLowerInvariant()).ToHashSet();
                var missing = emails.Where(e => !foundEmails.Contains(e)).ToList();

                if (missing.Count > 0)
                {
                    throw new ErrorException(
                        StatusCodes.Status404NotFound,
                        "USER_NOT_FOUND",
                        $"These emails do not exist: {string.Join(", ", missing)}");
                }

                var payload = JsonSerializer.Serialize(new { message = dto.Message });

                var now = DateTime.UtcNow;

                var createdDtos = new List<GetNotificationDTO>();

                foreach (var u in users)
                {
                    var entity = new Notification
                    {
                        NotificationId = Guid.NewGuid(), 
                        UserId = u.UserId,
                        Type = dto.Type.ToString(),
                        Channel = dto.Channel.ToString(),
                        Payload = payload,
                        SentAt = now
                    };

                    await notifRepo.InsertAsync(entity);

                    createdDtos.Add(new GetNotificationDTO
                    {
                        NotificationId = entity.NotificationId,
                        Type = entity.Type,
                        Channel = entity.Channel,
                        Payload = entity.Payload,
                        SentAt = entity.SentAt,
                        recipientEmailList = new List<string> { u.Email }
                    });
                }

                await _unitOfWork.SaveAsync();
                _unitOfWork.CommitTransaction();

                foreach (var item in createdDtos)
                {
                    var userId = users.First(x => x.Email.Equals(item.recipientEmailList[0], StringComparison.OrdinalIgnoreCase)).UserId;

                    await _hub.Clients
                        .Group($"notifications_{userId}")
                        .SendAsync("notification:new", item);
                }
            }
            catch (ErrorException)
            {
                _unitOfWork.RollBack();
                throw;
            }
            catch (Exception ex)
            {
                _unitOfWork.RollBack();
                throw new ErrorException(StatusCodes.Status500InternalServerError,
                    ResponseCodeConstants.INTERNAL_SERVER_ERROR,
                    $"Error creating Notifications: {ex.Message}");
            }
        }

        public async Task<PaginatedList<GetNotificationDTO>> GetMyNotificationsAsync(int pageNumber, int pageSize, Guid? idSearch)
        {
            if (pageNumber < 1 || pageSize < 1)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number or page size must be >= 1.");

            var userIdStr = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                throw new ErrorException(StatusCodes.Status401Unauthorized, ResponseCodeConstants.UNAUTHORIZED, "User not authenticated.");

            var repo = _unitOfWork.GetRepository<Notification>();

            IQueryable<Notification> q = repo.Entities
                .AsNoTracking()
                .Include(n => n.User)
                .Where(n => n.UserId == userId);

            if (idSearch.HasValue)
                q = q.Where(n => n.NotificationId == idSearch.Value);

            q = q.OrderByDescending(n => n.SentAt);

            var page = await repo.GetPagingAsync(q, pageNumber, pageSize);

            var items = page.Items.Select(n => new GetNotificationDTO
            {
                NotificationId = n.NotificationId,
                Type = n.Type,
                Channel = n.Channel,
                Payload = n.Payload,
                SentAt = n.SentAt,
                recipientEmailList = new List<string> { n.User.Email }
            }).ToList();

            return new PaginatedList<GetNotificationDTO>(items, page.TotalCount, page.PageNumber, page.PageSize);
        }

        public async Task<PaginatedList<GetNotificationDTO>> GetCreatedNotificationsAsync(int pageNumber, int pageSize, Guid? idSearch, string? recipientEmailSearch)
        {
            if (pageNumber < 1 || pageSize < 1)
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number or page size must be >= 1.");

            var repo = _unitOfWork.GetRepository<Notification>();

            IQueryable<Notification> q = repo.Entities
                .AsNoTracking()
                .Include(n => n.User);

            if (idSearch.HasValue)
                q = q.Where(n => n.NotificationId == idSearch.Value);

            if (!string.IsNullOrWhiteSpace(recipientEmailSearch))
            {
                var email = recipientEmailSearch.Trim().ToLowerInvariant();
                q = q.Where(n => n.User.Email.ToLower().Contains(email));
            }

            q = q.OrderByDescending(n => n.SentAt);

            var page = await repo.GetPagingAsync(q, pageNumber, pageSize);

            var items = page.Items.Select(n => new GetNotificationDTO
            {
                NotificationId = n.NotificationId,
                Type = n.Type,
                Channel = n.Channel,
                Payload = n.Payload,
                SentAt = n.SentAt,
                recipientEmailList = new List<string> { n.User.Email }
            }).ToList();

            return new PaginatedList<GetNotificationDTO>(items, page.TotalCount, page.PageNumber, page.PageSize);
        }
    }

}
