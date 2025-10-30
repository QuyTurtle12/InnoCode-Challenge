using Repository.DTOs.AuthDTOs;
using DataAccess.Entities;

namespace BusinessLogic.IServices.Users
{
    public interface IAuthService
    {
        Task<AuthResponseDTO> RegisterAsync(RegisterUserDTO dto);
        Task<AuthResponseDTO> LoginAsync(LoginDTO dto);
        Task<User?> GetCurrentLoggedInUser();
        Task<string> GenerateVerificationTokenAsync();
        Task VerifyEmailAsync(string token);
        Task ChangePasswordAsync(ChangePasswordDTO dto);
        Task<AuthResponseDTO> RefreshAsync(string refreshToken);
        Task LogoutAsync(string refreshToken);

        Task<string?> GenerateResetPasswordTokenAsync(string email);
        Task ResetPasswordAsync(ResetPasswordDTO dto);

    }
}
