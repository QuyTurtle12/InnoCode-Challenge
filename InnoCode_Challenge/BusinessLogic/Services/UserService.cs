using AutoMapper;
using Utility.Helpers;
using BusinessLogic.IServices;
using Repository.DTOs.UserDTOs;
using DataAccess.Entities;
using Utility.ExceptionCustom;
using Repository.IRepositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BusinessLogic.Services
{
    public class UserService : IUserService
    {
        private readonly IUOW _uow;
        private readonly IMapper _mapper;

        public UserService(IUOW uow, IMapper mapper)
        {
            _uow = uow;
            _mapper = mapper;
        }

        public async Task<IEnumerable<UserDTO>> GetAllUsersAsync()
        {
            var repo = _uow.GetRepository<User>();
            var users = await repo.Entities
                                  .Where(u => u.DeletedAt == null)
                                  .ToListAsync();
            return users.Select(u => _mapper.Map<UserDTO>(u));
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
            var repo = _uow.GetRepository<User>();

            // 1. Duplicate email?
            if (repo.Entities.Any(u => u.Email == dto.Email && !u.DeletedAt.HasValue))
                throw new ErrorException(
                  StatusCodes.Status400BadRequest,
                  "EMAIL_EXISTS",
                  "Email is already registered."
                );

            // 2. Map & insert with hashed password
            var userEntity = _mapper.Map<User>(dto);
            userEntity.PasswordHash = PasswordHasher.Hash(dto.Password);
            userEntity.Role = dto.Role;
            userEntity.CreatedAt = DateTime.UtcNow;
            userEntity.Status = dto.Status;

            await repo.InsertAsync(userEntity);
            await _uow.SaveAsync();

            return _mapper.Map<UserDTO>(userEntity);
        }

        public async Task<UserDTO> UpdateUserAsync(UpdateUserDTO dto)
        {
            var repo = _uow.GetRepository<User>();
            var user = await repo.GetByIdAsync(dto.Id);
            if (user == null)
                throw new ErrorException(
                  StatusCodes.Status404NotFound,
                  "USER_NOT_FOUND",
                  $"No user found with ID={dto.Id}"
                );

            // If email changed, check uniqueness
            if (!string.IsNullOrWhiteSpace(dto.Email)
                && dto.Email != user.Email
                && repo.Entities.Any(u => u.Email == dto.Email && u.UserId != dto.Id && !u.DeletedAt.HasValue))
            {
                throw new ErrorException(
                  StatusCodes.Status400BadRequest,
                  "EMAIL_EXISTS",
                  "Email is already in use by another account."
                );
            }

            // If role changed, convert to int
            if (!string.IsNullOrWhiteSpace(dto.Role))
            {
                user.Role = dto.Role;
            }

            // Update other non-null properties via AutoMapper
            _mapper.Map(dto, user);
            user.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(dto.Status))
                user.Status = dto.Status;

            // Save
            repo.Update(user);
            await _uow.SaveAsync();

            return _mapper.Map<UserDTO>(user);
        }

        public async Task DeleteUserAsync(Guid id, string deletedBy)
        {
            var repo = _uow.GetRepository<User>();
            var user = await repo.GetByIdAsync(id);
            if (user == null || user.DeletedAt != null)
                throw new ErrorException(
                    StatusCodes.Status404NotFound,
                    "USER_NOT_FOUND",
                    $"No user found with ID={id}"
                );

            user.DeletedAt = DateTime.UtcNow;
            repo.Update(user);
            await _uow.SaveAsync();
        }
    }
}
