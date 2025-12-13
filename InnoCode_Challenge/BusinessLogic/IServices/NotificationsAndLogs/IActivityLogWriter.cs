using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.IServices.NotificationsAndLogs
{
    public interface IActivityLogWriter
    {
        Task TryWriteAsync(Guid userId, string action, string? targetType = null, string? targetId = null);
    }
}
