using Repository.DTOs.UserDTOs;

namespace BusinessLogic.IServices
{
    public interface IUserService
    {
        Task<IEnumerable<UserDTO>> GetAllUsersAsync();
        Task<UserDTO> GetUserByIdAsync(Guid id);
        Task<UserDTO> CreateUserAsync(CreateUserDTO dto);
        Task<UserDTO> UpdateUserAsync(UpdateUserDTO dto);
        Task DeleteUserAsync(Guid id, string deletedBy);
    }
}
