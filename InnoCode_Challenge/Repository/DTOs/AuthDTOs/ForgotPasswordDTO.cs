using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repository.DTOs.AuthDTOs
{
    public class ForgotPasswordDTO
    {
        [Required, EmailAddress]
        public string Email { get; set; } = null!;
    }
}
