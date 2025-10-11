using AutoMapper;
using BusinessLogic.IServices;
using CloudinaryDotNet;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.NotificationDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.PaginatedList;

namespace BusinessLogic.Services
{
    public class NotificationService : INotificationService
    {
        private readonly IMapper _mapper;
        private readonly IUOW _unitOfWork;

        // Constructor
        public NotificationService(IMapper mapper, IUOW uow)
        {
            _mapper = mapper;
            _unitOfWork = uow;
        }

        public async Task CreateNotification(BaseNotificationDTO notificationDTO)
        {
            try
            {
                // Start a transaction
                _unitOfWork.BeginTransaction();

                // Get the repository for Notification entity
                IGenericRepository<Notification> notificationRepo = _unitOfWork.GetRepository<Notification>();

                // Generate a single notification ID to be shared by all recipients
                Guid sharedNotificationId = Guid.NewGuid();

                // Send notifications to each recipient
                foreach (string email in notificationDTO.recipientEmailList)
                {
                    // Create a new notification instance for each recipient
                    Notification notification = _mapper.Map<Notification>(notificationDTO);

                    // Use the shared notification ID for all instances
                    notification.NotificationId = sharedNotificationId;

                    // Handle specific properties based on the type of notification
                    if (notificationDTO is CreateGeneralNotificationDTO generalNotification)
                    {
                        // Handle general notification specific properties
                        notification.Payload = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            //senderId = generalNotification.SenderId,
                            //senderEmail = generalNotification.SenderEmail,
                            message = generalNotification.Message
                        });

                        // Find the UserId based on the email
                        notification.UserId = _unitOfWork.GetRepository<User>()
                            .Entities
                            .Where(u => u.Email == email)
                            .Select(u => u.UserId)
                            .FirstOrDefault();

                        notification.SentAt = DateTime.UtcNow;
                    }

                    // Add the notification to repository
                    await notificationRepo.InsertAsync(notification);
                }

                // Save changes
                await _unitOfWork.SaveAsync();

                // Commit the transaction
                _unitOfWork.CommitTransaction();
            }
            catch (Exception)
            {
                // If something fails, roll back the transaction
                _unitOfWork.RollBack();
                throw;
            }
        }

        public async Task<PaginatedList<GetNotificationDTO>> GetPaginatedCreatedNotificationsAsync(int pageNumber, int pageSize, Guid? idSearch, string? recipientEmailSearch)
        {
            // Validate pageNumber and pageSize
            if (pageNumber < 1 || pageSize < 1)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number or page size must be greater than or equal to 1.");
            }

            // Get the repository for Notification entity
            IGenericRepository<Notification> notificationRepo = _unitOfWork.GetRepository<Notification>();

            // Get notifications with their users
            IQueryable<Notification> query = notificationRepo.Entities.Include(n => n.User);

            // Apply id search filters if provided
            if (idSearch.HasValue)
            {
                query = query.Where(p => p.NotificationId.Equals(idSearch.Value));
            }

            // Apply Email filters if provided
            if (!string.IsNullOrWhiteSpace(recipientEmailSearch))
            {
                query = query.Where(p => p.User.Email.Equals(recipientEmailSearch));
            }

            // Get all the data we need
            var allNotifications = await query.ToListAsync();

            // Group by notification ID and other properties that uniquely identify a notification
            var groupedNotifications = allNotifications
                .GroupBy(n => new
                {
                    n.NotificationId,
                    n.Type,
                    n.Channel,
                    n.Payload,
                    n.SentAt
                })
                .Select(g => new GetNotificationDTO
                {
                    NotificationId = g.Key.NotificationId,
                    Type = g.Key.Type,
                    Channel = g.Key.Channel,
                    Payload = g.Key.Payload,
                    SentAt = g.Key.SentAt,
                    recipientEmailList = g.Select(n => n.User.Email).ToList()
                })
                .OrderByDescending(n => n.SentAt)
                .ToList();

            // Apply pagination manually after grouping
            int totalCount = groupedNotifications.Count;
            var paginatedItems = groupedNotifications
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Create a new paginated list with the grouped DTOs
            PaginatedList<GetNotificationDTO> paginatedList = new PaginatedList<GetNotificationDTO>(
                paginatedItems,
                totalCount,
                pageNumber,
                pageSize);

            return paginatedList;
        }

        public async Task<PaginatedList<GetNotificationDTO>> GetPaginatedNotificationAsync(int pageNumber, int pageSize, Guid? idSearch, string? recipientEmailSearch)
        {
            // Validate pageNumber and pageSize
            if (pageNumber < 1 || pageSize < 1)
            {
                throw new ErrorException(StatusCodes.Status400BadRequest, ResponseCodeConstants.BADREQUEST, "Page number or page size must be greater than or equal to 1.");
            }

            // Get the repository for Notification entity
            IGenericRepository<Notification> notificationRepo = _unitOfWork.GetRepository<Notification>();

            // Get notifications of current logged in user from database
            IQueryable<Notification> query = notificationRepo.Entities.Include(n => n.User);

            // Apply id search filters if provided
            if (idSearch.HasValue)
            {
                query = query.Where(p => p.NotificationId.Equals(idSearch.Value));
            }

            // Apply Email filters if provided
            if (!string.IsNullOrWhiteSpace(recipientEmailSearch))
            {
                query = query.Where(p => p.User.Email.Equals(recipientEmailSearch));
            }

            // Apply sorting by SentAt to get newest notifications first
            query = query.OrderByDescending(p => p.SentAt);

            // Change to paginated list to facilitate mapping process
            PaginatedList<Notification> resultQuery = await notificationRepo.GetPagging(query, pageNumber, pageSize);

            // Map the result to DTO
            IReadOnlyCollection<GetNotificationDTO> result = resultQuery.Items.Select(item =>
            {
                GetNotificationDTO notificationDTO = _mapper.Map<GetNotificationDTO>(item);

                notificationDTO.recipientEmailList.Add(item.User.Email);

                return notificationDTO;
            }).ToList();

            // Create a new paginated list with the mapped DTOs
            PaginatedList<GetNotificationDTO> paginatedList = new PaginatedList<GetNotificationDTO>(result, resultQuery.TotalCount, resultQuery.PageNumber, resultQuery.PageSize);

            // Return the paginated list of DTOs
            return paginatedList;
        }
    }
}
