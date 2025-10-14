using System.ComponentModel.DataAnnotations;
using Utility.Constant;

namespace Repository.DTOs.MentorDTOs
{
    public class UpdateMentorDTO
    {
        public Guid? SchoolId { get; set; }

        [MaxLength(20)]
        [RegularExpression(ValidationConstants.VietnamPhoneRegex, ErrorMessage = ValidationConstants.VietnamPhoneErrorMessage)]
        public string? Phone { get; set; }
    }
}
