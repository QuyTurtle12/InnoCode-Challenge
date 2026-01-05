using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utility.Constant;

namespace Repository.DTOs.RoleRegistrationDTOs
{
    public class CreateRoleRegistrationDTO
    {
        [Required]
        public string RequestedRole { get; set; } = null!; // staff | organizer | judge 

        [Required, MaxLength(150)]
        public string FullName { get; set; } = null!;

        [Required, EmailAddress, MaxLength(150)]
        public string Email { get; set; } = null!;

        [Required, MinLength(6)]
        public string Password { get; set; } = null!;

        [Required, MinLength(6)]
        public string ConfirmPassword { get; set; } = null!;

        [RegularExpression(ValidationConstants.VietnamPhoneRegex, ErrorMessage = ValidationConstants.VietnamPhoneErrorMessage)]
        [MaxLength(11)]
        public string? Phone { get; set; }

        public string? Payload { get; set; }

        // evidences: images/pdf
        [Required]
        public IList<IFormFile> EvidenceFiles { get; set; } = new List<IFormFile>();
    }
}
