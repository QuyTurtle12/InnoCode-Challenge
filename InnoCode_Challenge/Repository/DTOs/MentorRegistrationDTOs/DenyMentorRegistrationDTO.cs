using System.ComponentModel.DataAnnotations;

namespace Repository.DTOs.MentorRegistrationDTOs
{
    public class DenyMentorRegistrationDTO
    {
        [Required, MaxLength(300)]
        public string Reason { get; set; } = null!;
    }
}
