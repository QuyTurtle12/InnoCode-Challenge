namespace Repository.DTOs.AuthDTOs
{
    public class MentorRegistrationAckDTO
    {
        public Guid UserId { get; set; }
        public string Fullname { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Status { get; set; } = "Inactive"; 
        public string Message { get; set; } = "Registration received. A staff member will review and activate your account.";
    }
}
