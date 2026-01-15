using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.RoleRegistrationDTOs
{
    public class RoleRegistrationSubmittedDTO
    {
        public Guid RegistrationId { get; set; }
        public string Status { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
    }
}
