using BusinessLogic.Hubs;
using BusinessLogic.IServices.NotificationsAndLogs;
using DataAccess.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Repository.IRepositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Services.NotificationsAndLogs
{
    public class ActivityLogWriter : IActivityLogWriter
    {
        private readonly IUOW _uow;
        private readonly ILogger<ActivityLogWriter> _logger;
        private readonly IHubContext<ActivityLogsHub> _hub;

        public ActivityLogWriter(IUOW uow, ILogger<ActivityLogWriter> logger, IHubContext<ActivityLogsHub> hub)
        {
            _uow = uow;
            _logger = logger;
            _hub = hub;
        }

        public async Task TryWriteAsync(Guid userId, string action, string? targetType = null, string? targetId = null)
        {
            if (userId == Guid.Empty) return;
            if (string.IsNullOrWhiteSpace(action)) return;

            try
            {
                var repo = _uow.GetRepository<ActivityLog>();

                var entity = new ActivityLog
                {
                    LogId = Guid.NewGuid(),
                    UserId = userId,
                    Action = action.Trim(),
                    TargetType = string.IsNullOrWhiteSpace(targetType) ? null : targetType.Trim(),
                    TargetId = string.IsNullOrWhiteSpace(targetId) ? null : targetId.Trim(),
                    At = DateTime.UtcNow
                };

                await repo.InsertAsync(entity);
                await _uow.SaveAsync();

                await _hub.Clients.Group("activity_logs").SendAsync("activitylog:new", new
                {
                    logId = entity.LogId,
                    userId = entity.UserId,
                    action = entity.Action,
                    targetType = entity.TargetType,
                    targetId = entity.TargetId,
                    at = entity.At
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to write activity log. UserId={UserId}, Action={Action}, TargetType={TargetType}, TargetId={TargetId}",
                    userId, action, targetType, targetId);
            }
        }
    }
}