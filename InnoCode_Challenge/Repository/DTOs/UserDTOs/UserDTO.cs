namespace Repository.DTOs.UserDTOs
{
    public class UserDTO
    {
        public Guid UserId { get; set; }
        public string Fullname { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Role { get; set; } = null!; // “User”, “Staff”, or “Admin”
        public string Status { get; set; } = "Active"; // “Active” or “Inactive”
    }
}
