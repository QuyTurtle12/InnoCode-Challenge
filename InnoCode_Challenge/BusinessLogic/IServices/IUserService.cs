using Repository.DTOs.UserDTOs;
using Repository.DTOs.UserDTOs.Repository.DTOs.UserDTOs;
using Utility.PaginatedList;

namespace BusinessLogic.IServices
{
    public interface IUserService
    {
        Task<PaginatedList<UserDTO>> GetUsersAsync(UserQueryParams query);
        Task<UserDTO> GetUserByIdAsync(Guid id);
        Task<UserDTO> CreateUserAsync(CreateUserDTO dto);
        Task<UserDTO> UpdateUserAsync(Guid id, UpdateUserDTO dto, string performedByRole);
        Task DeleteUserAsync(Guid id, string deletedBy);
    }
}
