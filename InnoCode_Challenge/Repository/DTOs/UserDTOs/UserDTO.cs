using System.ComponentModel.DataAnnotations;
using Utility.Constant;

namespace Repository.DTOs.UserDTOs
{
    public class UserDTO
    {
        public Guid UserId { get; set; }
        public string Fullname { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Role { get; set; } = null!;

        [RegularExpression(UserStatusConstants.StatusRegexPattern,
    ErrorMessage = UserStatusConstants.StatusRegexErrorMessage)]

        public string Status { get; set; } = UserStatusConstants.Active;
    }
}
