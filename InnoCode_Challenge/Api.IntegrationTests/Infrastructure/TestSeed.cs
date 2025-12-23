using DataAccess.Entities;
using Utility.Constant;
using Utility.Helpers;

namespace Api.IntegrationTests.Infrastructure
{
    public static class TestSeed
    {
        // Lưu lại ID để test dùng
        public static Guid SchoolId { get; private set; }
        private static readonly Guid DefaultSchoolId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static string AdminEmail { get; private set; } = "admin@test.com";
        public static string AdminPassword { get; private set; } = "P@ssword123!";

        public static void Seed(ContestDbContext db)
        {
            var now = DateTime.UtcNow;

            var school = db.Schools.FirstOrDefault(s => s.SchoolId == DefaultSchoolId);
            if (school == null)
            {
                school = new School
                {
                    SchoolId = DefaultSchoolId,
                    Name = "Test School",
                    ProvinceId = Guid.Parse("2ef61ca0-ed88-4a43-b842-7c0d059e3506"),
                    CreatedAt = now,
                    DeletedAt = null
                };
                db.Schools.Add(school);
            }
            SchoolId = school.SchoolId;

            if (!db.Users.Any(u => u.Email == AdminEmail.ToLower() && u.DeletedAt == null))
            {
                db.Users.Add(new User
                {
                    UserId = Guid.NewGuid(),
                    Fullname = "Test Admin",
                    Email = AdminEmail.Trim().ToLowerInvariant(),
                    PasswordHash = PasswordHasher.Hash(AdminPassword),
                    Role = RoleConstants.Admin,
                    Status = UserStatusConstants.Active,
                    CreatedAt = now,
                    UpdatedAt = now,
                    DeletedAt = null
                });
            }

            db.SaveChanges();
        }
    }
}
