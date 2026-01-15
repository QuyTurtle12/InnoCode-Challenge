using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.RoleRegistrationDTOs
{
    public class DenyRoleRegistrationDTO
    {
        [Required, MaxLength(500)]
        public string Reason { get; set; } = null!;
    }
}
