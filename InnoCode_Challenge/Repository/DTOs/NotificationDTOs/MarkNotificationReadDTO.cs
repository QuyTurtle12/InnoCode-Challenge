using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.NotificationDTOs
{
    public class MarkNotificationReadDTO
    {
        [Required]
        public Guid NotificationId { get; set; }
    }

}
