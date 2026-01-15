using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility.Constant
{
    public static class UserStatusConstants
    {
        public const string Active = "Active";
        public const string Inactive = "Inactive";
        public const string Locked = "Locked";
        public const string Unverified = "Unverified";

        public const string StatusRegexPattern = "^(Active|Inactive|Locked|Unverified)$";
        public const string StatusRegexErrorMessage = "Status must be one of: Active, Inactive, Locked, Unverified.";
    }

}
