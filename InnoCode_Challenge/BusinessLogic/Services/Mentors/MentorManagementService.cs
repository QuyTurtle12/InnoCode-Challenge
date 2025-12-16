using BusinessLogic.IServices.Mentors;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.MentorManagementDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.Helpers;

namespace BusinessLogic.Services.Mentors
{
    public class MentorManagementService : IMentorManagementService
    {
        private readonly IUOW _uow;

        public MentorManagementService(IUOW uow)
        {
            _uow = uow;
        }

        private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

        public async Task<MentorProfileDTO> CreateMentorAsync(Guid schoolId, MentorManagerRequestDTO dto, Guid requesterUserId)
        {
            if (!string.Equals(dto.Password, dto.ConfirmPassword, StringComparison.Ordinal))
                throw new ErrorException(StatusCodes.Status400BadRequest, "CONFIRM_PASSWORD_MISMATCH", "Passwords do not match.");

            var schoolRepo = _uow.GetRepository<School>();
            var userRepo = _uow.GetRepository<User>();
            var mentorRepo = _uow.GetRepository<Mentor>();

            var school = await schoolRepo.Entities.FirstOrDefaultAsync(s => s.SchoolId == schoolId && s.DeletedAt == null)
                ?? throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND", "School not found.");

            if (school.ManagerUserId != requesterUserId)
                throw new ErrorException(StatusCodes.Status403Forbidden, "FORBIDDEN", "Only the school manager can create mentors for this school.");

            var email = NormalizeEmail(dto.Email);

            var emailExists = await userRepo.Entities.AnyAsync(u => u.Email.ToLower() == email && u.DeletedAt == null);
            if (emailExists)
                throw new ErrorException(StatusCodes.Status409Conflict, "EMAIL_EXISTS", "Email is already registered.");

            var now = DateTime.UtcNow;

            _uow.BeginTransaction();
            try
            {
                var user = new User
                {
                    UserId = Guid.NewGuid(),
                    Email = email,
                    Fullname = dto.Fullname.Trim(),
                    PasswordHash = PasswordHasher.Hash(dto.Password),
                    Role = RoleConstants.Mentor,
                    Status = UserStatusConstants.Active,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                await userRepo.InsertAsync(user);
                await _uow.SaveAsync();

                var mentor = new Mentor
                {
                    MentorId = Guid.NewGuid(),
                    UserId = user.UserId,
                    SchoolId = schoolId,
                    Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim(),
                    CreatedAt = now,
                    CreatedBy = requesterUserId
                };

                await mentorRepo.InsertAsync(mentor);
                await _uow.SaveAsync();

                _uow.CommitTransaction();

                return new MentorProfileDTO
                {
                    MentorId = mentor.MentorId,
                    UserId = user.UserId,
                    SchoolId = schoolId,
                    Email = user.Email,
                    Fullname = user.Fullname,
                    Role = user.Role,
                    CreatedAt = mentor.CreatedAt,
                    CreatedBy = mentor.CreatedBy
                };
            }
            catch
            {
                _uow.RollBack();
                throw;
            }
        }
    }
}
