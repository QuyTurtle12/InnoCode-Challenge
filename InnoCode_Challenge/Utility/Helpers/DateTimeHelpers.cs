using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility.Helpers
{
    public static class DateTimeHelpers
    {
        /// <summary>
        /// Formats datetime to ISO 8601 format with UTC indicator: yyyy-MM-ddTHH:mm:ssZ
        /// </summary>
        public static string ToIso8601String(DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-ddTHH:mm:ss") + "Z";
        }
    }
}
