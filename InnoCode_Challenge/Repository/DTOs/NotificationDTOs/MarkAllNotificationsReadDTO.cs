using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.NotificationDTOs
{
    public class MarkAllNotificationsReadDTO
    {
        //read all up to now
        public DateTime? UpTo { get; set; }
    }
}
