namespace Repository.DTOs.AuthDTOs
{
    public class AuthResponseDTO
    {
        public string Token { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
        public string UserId { get; set; } = null!;
        public string Role { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string RefreshToken { get; set; } = null!;
        public DateTime RefreshExpiresAt { get; set; }
        public bool EmailVerified { get; set; }

    }
}
