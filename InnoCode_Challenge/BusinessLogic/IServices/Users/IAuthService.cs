using DataAccess.Entities;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.AuthDTOs.Repository.DTOs.AuthDTOs;

namespace BusinessLogic.IServices.Users
{
    public interface IAuthService
    {
        Task<AuthResponseDTO> RegisterStudentStrictAsync(RegisterStudentDTO dto);
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

        Task<ProfileDTO> RegisterJudgeAsync(RegisterUserDTO dto);
        Task<ProfileDTO> RegisterAdminAsync(RegisterUserDTO dto);
        Task<ProfileDTO> RegisterStaffAsync(RegisterUserDTO dto);
        Task<ProfileDTO> RegisterOrganizerAsync(RegisterUserDTO dto);

    }
}
