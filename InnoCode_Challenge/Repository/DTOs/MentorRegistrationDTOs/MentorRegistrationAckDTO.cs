using System;

namespace Repository.DTOs.MentorRegistrationDTOs
{
    public class MentorRegistrationAckDTO
    {
        public Guid RegistrationId { get; set; }
        public string Fullname { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string Message { get; set; } = null!;
    }
}