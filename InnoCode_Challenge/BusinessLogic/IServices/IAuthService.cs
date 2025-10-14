using Repository.DTOs.AuthDTOs;
using DataAccess.Entities;

namespace BusinessLogic.IServices
{
    public interface IAuthService
    {
        Task<AuthResponseDTO> RegisterAsync(RegisterUserDTO dto);
        Task<AuthResponseDTO> LoginAsync(LoginDTO dto);
        Task<MentorRegistrationAckDTO> RegisterMentorAsync(RegisterMentorDTO dto);

        Task<User?> GetCurrentLoggedInUser();
    }
}
