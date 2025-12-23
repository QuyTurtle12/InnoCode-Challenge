using DataAccess.Entities;
using Utility.Constant;
using Utility.Helpers;

namespace Api.IntegrationTests.Infrastructure
{
    public static class TestSeed
    {
        // Lưu lại ID để test dùng
        public static Guid SchoolId { get; private set; }
        public static string AdminEmail { get; private set; } = "admin@test.com";
        public static string AdminPassword { get; private set; } = "P@ssword123!";

        public static void Seed(ContestDbContext db)
        {
            var now = DateTime.UtcNow;

            if (!db.Schools.Any())
            {
                var school = new School
                {
                    SchoolId = Guid.NewGuid(),
                    Name = "Test School",
                    ProvinceId = Guid.Parse("2ef61ca0-ed88-4a43-b842-7c0d059e3506"),
                    CreatedAt = now,
                    DeletedAt = null
                };
                SchoolId = school.SchoolId;
                db.Schools.Add(school);
            }
            else
            {
                SchoolId = db.Schools.Select(s => s.SchoolId).First();
            }

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