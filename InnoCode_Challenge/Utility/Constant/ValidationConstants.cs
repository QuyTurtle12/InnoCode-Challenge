using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace Utility.Constant
{
    public static class ValidationConstants
    {
        public const string VietnamPhoneRegex = @"^0\d{9,10}$";
        public const string VietnamPhoneErrorMessage = "Phone must start with 0 and be 10–11 digits.";
    }
}
