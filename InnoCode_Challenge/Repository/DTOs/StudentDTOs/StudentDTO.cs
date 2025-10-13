namespace Repository.DTOs.StudentDTOs
{
    public class StudentDTO
    {
        public Guid StudentId { get; set; }
        public Guid UserId { get; set; }
        public string UserFullname { get; set; } = null!;
        public string UserEmail { get; set; } = null!;
        public Guid SchoolId { get; set; }
        public string SchoolName { get; set; } = null!;
        public string? Grade { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
