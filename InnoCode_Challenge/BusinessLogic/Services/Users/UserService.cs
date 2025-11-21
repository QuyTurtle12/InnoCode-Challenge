using AutoMapper;
using BusinessLogic.IServices.Users;
using DataAccess.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Repository.DTOs.UserDTOs;
using Repository.IRepositories;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.Helpers;
using Utility.PaginatedList;

namespace BusinessLogic.Services.Users
{
    public class UserService : IUserService
    {
        private static readonly string[] AllowedRoles =
{
            RoleConstants.Student,
            RoleConstants.Mentor,
            RoleConstants.Judge,
            RoleConstants.Staff,
            RoleConstants.Admin,
            RoleConstants.ContestOrganizer
        };

        private static readonly string[] AllowedStatuses = { "Active", "Inactive", "Locked" };


        private readonly IUOW _uow;
        private readonly IMapper _mapper;

        public UserService(IUOW uow, IMapper mapper)
        {
            _uow = uow;
            _mapper = mapper;
        }

        public async Task<PaginatedList<UserDTO>> GetUsersAsync(UserQueryParams query)
        {
            var repo = _uow.GetRepository<User>();
            var users = repo.Entities
                                  .Where(u => u.DeletedAt == null)
                                  .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var keyword = query.Search.Trim().ToLower();
                users = users.Where(u => u.Fullname.ToLower().Contains(keyword) || u.Email.ToLower().Contains(keyword));
            }

            if (!string.IsNullOrWhiteSpace(query.Role))
                users = users.Where(u => u.Role == query.Role);

            if (!string.IsNullOrWhiteSpace(query.Status))
                users = users.Where(u => u.Status == query.Status);

            users = (query.SortBy?.ToLowerInvariant()) switch
            {
                "updatedat" => query.Desc ? users.OrderByDescending(u => u.UpdatedAt) : users.OrderBy(u => u.UpdatedAt),
                "fullname" => query.Desc ? users.OrderByDescending(u => u.Fullname) : users.OrderBy(u => u.Fullname),
                "email" => query.Desc ? users.OrderByDescending(u => u.Email) : users.OrderBy(u => u.Email),
                "role" => query.Desc ? users.OrderByDescending(u => u.Role) : users.OrderBy(u => u.Role),
                "status" => query.Desc ? users.OrderByDescending(u => u.Status) : users.OrderBy(u => u.Status),
                _ => query.Desc ? users.OrderByDescending(u => u.CreatedAt) : users.OrderBy(u => u.CreatedAt),
            };

            var page = await repo.GetPagingAsync(users, query.Page, query.PageSize);
            var mapped = page.Items.Select(_mapper.Map<UserDTO>).ToList();
            return new PaginatedList<UserDTO>(mapped.ToList(), page.TotalCount, page.PageNumber, page.PageSize);

        }

        public async Task<UserDTO> GetUserByIdAsync(Guid id)
        {
            var repo = _uow.GetRepository<User>();
            var user = await repo.GetByIdAsync(id);
            if (user == null || user.DeletedAt != null)
                throw new ErrorException(
                    StatusCodes.Status404NotFound,
                    "USER_NOT_FOUND",
                    $"No user found with ID={id}"
                );

            return _mapper.Map<UserDTO>(user);
        }

        public async Task<UserDTO> CreateUserAsync(CreateUserDTO dto)
        {

            ValidateRole(dto.Role);
            ValidateStatus(dto.Status);

            var email = NormalizeEmail(dto.Email);

            var repo = _uow.GetRepository<User>();

            // 1. Duplicate email?
            if (await repo.Entities.AnyAsync(u => u.Email.ToLower() == email && u.DeletedAt == null))
                throw new ErrorException(StatusCodes.Status400BadRequest, "EMAIL_EXISTS", "Email is already registered.");

            // 2. Map & insert with hashed password
            var entity = _mapper.Map<User>(dto);
            entity.Email = email;
            entity.PasswordHash = PasswordHasher.Hash(dto.Password);
            entity.Fullname = dto.Fullname.Trim();
            entity.Role = dto.Role;
            entity.Status = string.IsNullOrWhiteSpace(dto.Status) ? "Active" : dto.Status;
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;

            await repo.InsertAsync(entity);
            await _uow.SaveAsync();

            return _mapper.Map<UserDTO>(entity);
        }

        public async Task<UserDTO> UpdateUserAsync(Guid id, UpdateUserDTO dto, string performedByRole)
        {
            var repo = _uow.GetRepository<User>();
            var user = await repo.GetByIdAsync(id);

            if (user == null || user.DeletedAt != null)
                throw new ErrorException(StatusCodes.Status404NotFound, "USER_NOT_FOUND", $"No user found with ID={id}");

            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                var email = NormalizeEmail(dto.Email);
                if (!string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
                {
                    var exists = await repo.Entities.AnyAsync(u => u.Email.ToLower() == email && u.UserId != id && u.DeletedAt == null);
                    if (exists)
                        throw new ErrorException(StatusCodes.Status400BadRequest, "EMAIL_EXISTS", "Email is already in use by another account.");
                    user.Email = email;
                }
            }


            if (!string.IsNullOrWhiteSpace(dto.Status))
            {
                ValidateStatus(dto.Status);
                user.Status = dto.Status;
            }

            if (!string.IsNullOrWhiteSpace(dto.Fullname))
            {
                user.Fullname = dto.Fullname.Trim();
            }

            if (!string.IsNullOrWhiteSpace(dto.NewPassword))
            {
                EnsureAdmin(performedByRole);
                user.PasswordHash = PasswordHasher.Hash(dto.NewPassword);
            }

            if (!string.IsNullOrWhiteSpace(dto.Role))
            {
                EnsureAdmin(performedByRole);
                ValidateRole(dto.Role);
                user.Role = dto.Role;
            }


            user.UpdatedAt = DateTime.UtcNow;
            repo.Update(user);
            await _uow.SaveAsync();

            return _mapper.Map<UserDTO>(user);
        }

        public async Task DeleteUserAsync(Guid id, string deletedBy)
        {
            var repo = _uow.GetRepository<User>();
            var user = await repo.GetByIdAsync(id);
            if (user == null || user.DeletedAt != null)
                throw new ErrorException(StatusCodes.Status404NotFound, "USER_NOT_FOUND", $"No user found with ID={id}");

            user.DeletedAt = DateTime.UtcNow;
            repo.Update(user);
            await _uow.SaveAsync();
        }

        private static string NormalizeEmail(string email)
            => email.Trim().ToLowerInvariant();

        private static void ValidateRole(string role)
        {
            if (!AllowedRoles.Contains(role))
                throw new ErrorException(StatusCodes.Status400BadRequest, "INVALID_ROLE", $"Role '{role}' is not allowed.");
        }

        private static void ValidateStatus(string status)
        {
            if (!AllowedStatuses.Contains(status))
                throw new ErrorException(StatusCodes.Status400BadRequest, "INVALID_STATUS", $"Status '{status}' is not allowed.");
        }

        private static void EnsureAdmin(string performedByRole)
        {
            if (!string.Equals(performedByRole, RoleConstants.Admin, StringComparison.Ordinal))
                throw new ErrorException(StatusCodes.Status403Forbidden, "FORBIDDEN", "Only Admin can perform this operation.");
        }


    }
}
