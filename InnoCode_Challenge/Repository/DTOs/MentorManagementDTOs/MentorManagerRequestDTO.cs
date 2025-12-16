using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.MentorManagementDTOs
{
    public class MentorManagerRequestDTO
    {
        [Required] public string Fullname { get; set; } = null!;
        [Required] public string Email { get; set; } = null!;
        [Required] public string Password { get; set; } = null!;
        [Required] public string ConfirmPassword { get; set; } = null!;
        public string? Phone { get; set; }
    }


}
