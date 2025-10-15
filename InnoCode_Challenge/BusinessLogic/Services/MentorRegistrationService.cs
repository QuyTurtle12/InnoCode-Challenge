using AutoMapper;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using BusinessLogic.IServices;
using Repository.IRepositories;
using Repository.DTOs.MentorRegistrationDTOs;
using Utility.ExceptionCustom;
using Utility.Helpers;
using Utility.PaginatedList;
using Utility.Constant;

namespace BusinessLogic.Services
{
    public class MentorRegistrationService : IMentorRegistrationService
    {
        private readonly IUOW _uow;
        private readonly IMapper _mapper;

        private static readonly string[] AllowedStatuses = { "pending", "approved", "denied" };

        public MentorRegistrationService(IUOW uow, IMapper mapper)
        {
            _uow = uow;
            _mapper = mapper;
        }

        public async Task<MentorRegistrationAckDTO> SubmitAsync(RegisterMentorDTO dto)
        {
            var email = NormalizeEmail(dto.Email);

            var userRepo = _uow.GetRepository<User>();
            var regRepo = _uow.GetRepository<MentorRegistration>();
            var schoolRepo = _uow.GetRepository<School>();
            var provinceRepo = _uow.GetRepository<Province>();

            bool userExists = await userRepo.Entities
                .AnyAsync(u => u.Email.ToLower() == email && u.DeletedAt == null);
            if (userExists)
                throw new ErrorException(StatusCodes.Status400BadRequest, "EMAIL_EXISTS",
                    "Email is already registered. Please log in or reset your password.");

            bool pendingRegExists = await regRepo.Entities
                .AnyAsync(r => r.Email.ToLower() == email
                               && r.DeletedAt == null
                               && r.Status == "pending");
            if (pendingRegExists)
                throw new ErrorException(StatusCodes.Status409Conflict, "PENDING_EXISTS",
                    "A pending registration for this email already exists.");

            if (dto.SchoolId == null)
            {
                if (string.IsNullOrWhiteSpace(dto.ProposedSchoolName) || dto.ProvinceId == null)
                    throw new ErrorException(StatusCodes.Status400BadRequest, "INVALID_SCHOOL_INPUT",
                        "Provide an existing SchoolId OR proposed school name + province.");
                // province must exist
                bool provinceOk = await provinceRepo.Entities.AnyAsync(p => p.ProvinceId == dto.ProvinceId);
                if (!provinceOk)
                    throw new ErrorException(StatusCodes.Status404NotFound, "PROVINCE_NOT_FOUND",
                        $"No province with ID={dto.ProvinceId}");
            }
            else
            {
                // existing school must exist
                bool schoolOk = await schoolRepo.Entities
                    .AnyAsync(s => s.SchoolId == dto.SchoolId && s.DeletedAt == null);
                if (!schoolOk)
                    throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND",
                        $"No school with ID={dto.SchoolId}");
            }

            var now = DateTime.UtcNow;
            var reg = new MentorRegistration
            {
                RegistrationId = Guid.NewGuid(),
                Fullname = dto.Fullname.Trim(),
                Email = email,
                PasswordHash = PasswordHasher.Hash(dto.Password), // store now; or set on approval if you prefer
                Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim(),
                SchoolId = dto.SchoolId,
                ProposedSchoolName = string.IsNullOrWhiteSpace(dto.ProposedSchoolName) ? null : dto.ProposedSchoolName.Trim(),
                ProposedSchoolAddress = string.IsNullOrWhiteSpace(dto.ProposedSchoolAddress) ? null : dto.ProposedSchoolAddress.Trim(),
                ProvinceId = dto.ProvinceId,
                Status = "pending",
                CreatedAt = now
            };

            await regRepo.InsertAsync(reg);
            await _uow.SaveAsync();

            return new MentorRegistrationAckDTO
            {
                RegistrationId = reg.RegistrationId,
                Fullname = reg.Fullname,
                Email = reg.Email,
                Status = reg.Status,
                Message = "Registration received. Staff will review and respond via email."
            };
        }

        public async Task<PaginatedList<MentorRegistrationDTO>> GetAsync(MentorRegistrationQueryParams query)
        {
            var repo = _uow.GetRepository<MentorRegistration>();
            var q = repo.Entities.Where(r => r.DeletedAt == null).AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                var s = query.Status.Trim().ToLowerInvariant();
                q = q.Where(r => r.Status == s);
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var k = query.Search.Trim().ToLowerInvariant();
                q = q.Where(r => r.Fullname.ToLower().Contains(k) || r.Email.ToLower().Contains(k) || (r.Phone != null && r.Phone.ToLower().Contains(k)));
            }

            q = (query.SortBy?.ToLowerInvariant()) switch
            {
                "fullname" => query.Desc ? q.OrderByDescending(r => r.Fullname) : q.OrderBy(r => r.Fullname),
                "email" => query.Desc ? q.OrderByDescending(r => r.Email) : q.OrderBy(r => r.Email),
                "status" => query.Desc ? q.OrderByDescending(r => r.Status) : q.OrderBy(r => r.Status),
                _ => query.Desc ? q.OrderByDescending(r => r.CreatedAt) : q.OrderBy(r => r.CreatedAt),
            };

            var page = await _uow.GetRepository<MentorRegistration>().GetPagingAsync(q, query.Page, query.PageSize);
            var items = page.Items.Select(_mapper.Map<MentorRegistrationDTO>).ToList();
            return new PaginatedList<MentorRegistrationDTO>(items, page.TotalCount, page.PageNumber, page.PageSize);
        }

        public async Task<MentorRegistrationDTO> GetByIdAsync(Guid id)
        {
            var repo = _uow.GetRepository<MentorRegistration>();
            var reg = await repo.Entities.AsNoTracking().FirstOrDefaultAsync(r => r.RegistrationId == id && r.DeletedAt == null);
            if (reg == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "REGISTRATION_NOT_FOUND", $"No registration with ID={id}");
            return _mapper.Map<MentorRegistrationDTO>(reg);
        }

        public async Task<MentorRegistrationDTO> ApproveAsync(Guid id, ApproveMentorRegistrationDTO dto, Guid staffUserId)
        {
            var regRepo = _uow.GetRepository<MentorRegistration>();
            var userRepo = _uow.GetRepository<User>();
            var mentorRepo = _uow.GetRepository<Mentor>();
            var schoolRepo = _uow.GetRepository<School>();
            var provinceRepo = _uow.GetRepository<Province>();

            var reg = await regRepo.Entities.FirstOrDefaultAsync(r => r.RegistrationId == id && r.DeletedAt == null);
            if (reg == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "REGISTRATION_NOT_FOUND", $"No registration with ID={id}");
            if (reg.Status != "pending")
                throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Only pending registrations can be approved.");

            var email = reg.Email.Trim().ToLowerInvariant();

            // 1) Email must still be free
            bool userExists = await userRepo.Entities.AnyAsync(u => u.Email.ToLower() == email && u.DeletedAt == null);
            if (userExists)
                throw new ErrorException(StatusCodes.Status409Conflict, "EMAIL_TAKEN", "Email is already in use.");

            _uow.BeginTransaction();
            try
            {
                // 2) Resolve school to use (existing logic unchanged) ----------------------
                Guid schoolIdToUse;
                if (dto.SchoolId.HasValue)
                {
                    bool exists = await schoolRepo.Entities.AnyAsync(s => s.SchoolId == dto.SchoolId.Value && s.DeletedAt == null);
                    if (!exists)
                        throw new ErrorException(StatusCodes.Status404NotFound, "SCHOOL_NOT_FOUND", $"No school with ID={dto.SchoolId.Value}");
                    schoolIdToUse = dto.SchoolId.Value;
                }
                else if (reg.SchoolId.HasValue)
                {
                    schoolIdToUse = reg.SchoolId.Value;
                }
                else if (dto.UseProposedSchool && !string.IsNullOrWhiteSpace(reg.ProposedSchoolName) && reg.ProvinceId.HasValue)
                {
                    var now = DateTime.UtcNow;
                    var newSchool = new School
                    {
                        SchoolId = Guid.NewGuid(),
                        Name = reg.ProposedSchoolName!,
                        ProvinceId = reg.ProvinceId!.Value,
                        Contact = reg.ProposedSchoolAddress,
                        CreatedAt = now
                    };

                    bool provinceOk = await provinceRepo.Entities.AnyAsync(p => p.ProvinceId == newSchool.ProvinceId);
                    if (!provinceOk)
                        throw new ErrorException(StatusCodes.Status404NotFound, "PROVINCE_NOT_FOUND", $"No province with ID={newSchool.ProvinceId}");

                    await schoolRepo.InsertAsync(newSchool);
                    schoolIdToUse = newSchool.SchoolId;
                }
                else
                {
                    throw new ErrorException(StatusCodes.Status400BadRequest, "NO_SCHOOL_RESOLUTION",
                        "Neither existing school nor valid proposed school available to approve.");
                }

                var baseName = (reg.Fullname ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(baseName))
                    baseName = "Mentor";

                var uniqueFullname = await GenerateUniqueFullnameAsync(baseName, userRepo);

                var now2 = DateTime.UtcNow;
                var user = new User
                {
                    UserId = Guid.NewGuid(),
                    Fullname = uniqueFullname,
                    Email = email,
                    PasswordHash = reg.PasswordHash ?? PasswordHasher.Hash(Guid.NewGuid().ToString("N")),
                    Role = RoleConstants.Mentor,
                    Status = "Active",
                    CreatedAt = now2,
                    UpdatedAt = now2
                };
                await userRepo.InsertAsync(user);

                var mentor = new Mentor
                {
                    MentorId = Guid.NewGuid(),
                    UserId = user.UserId,
                    SchoolId = schoolIdToUse,
                    Phone = reg.Phone,
                    CreatedAt = now2
                };
                await mentorRepo.InsertAsync(mentor);

                reg.Status = "approved";
                reg.ReviewedByUserId = staffUserId;
                reg.ReviewedAt = now2;
                regRepo.Update(reg);

                await _uow.SaveAsync();
                _uow.CommitTransaction();

                return _mapper.Map<MentorRegistrationDTO>(reg);
            }
            catch
            {
                _uow.RollBack();
                throw;
            }
        }
        private static async Task<string> GenerateUniqueFullnameAsync(string baseName, IGenericRepository<User> userRepo)
        {
            var name = baseName;
            int suffix = 2;

            // Case-insensitive check
            while (await userRepo.Entities.AnyAsync(u => u.DeletedAt == null &&
                                                         u.Fullname.ToLower() == name.ToLower()))
            {
                name = $"{baseName} {suffix++}";
                // Optional: cap attempts to avoid infinite loop
                if (suffix > 9999)
                    throw new ErrorException(StatusCodes.Status409Conflict, "FULLNAME_EXISTS",
                        "Could not generate a unique display name.");
            }
            return name;
        }


        public async Task<MentorRegistrationDTO> DenyAsync(Guid id, DenyMentorRegistrationDTO dto, Guid staffUserId)
        {
            var regRepo = _uow.GetRepository<MentorRegistration>();
            var reg = await regRepo.Entities.FirstOrDefaultAsync(r => r.RegistrationId == id && r.DeletedAt == null);
            if (reg == null)
                throw new ErrorException(StatusCodes.Status404NotFound, "REGISTRATION_NOT_FOUND", $"No registration with ID={id}");
            if (reg.Status != "pending")
                throw new ErrorException(StatusCodes.Status409Conflict, "INVALID_STATE", "Only pending registrations can be denied.");

            reg.Status = "denied";
            reg.DenyReason = dto.Reason.Trim();
            reg.ReviewedByUserId = staffUserId;
            reg.ReviewedAt = DateTime.UtcNow;

            regRepo.Update(reg);
            await _uow.SaveAsync();

            return _mapper.Map<MentorRegistrationDTO>(reg);
        }

        private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
    }
}
