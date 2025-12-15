using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.NotificationDTOs
{
    public class MarkReadResultDTO
    {
        public int UpdatedCount { get; set; }
        public DateTime ReadAt { get; set; }
    }
}
