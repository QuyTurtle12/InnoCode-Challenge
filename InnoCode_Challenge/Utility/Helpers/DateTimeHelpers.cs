using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility.Helpers
{
    public static class DateTimeHelpers
    {
        // UTC+7 timezone offset
        private static readonly TimeSpan UTC_PLUS_7_OFFSET = TimeSpan.FromHours(7);

        /// <summary>
        /// Converts UTC+7 datetime to UTC (for saving to database)
        /// </summary>
        public static DateTime ConvertToUtc(DateTime dateTime)
        {
            // If already UTC, return as is
            if (dateTime.Kind == DateTimeKind.Utc)
                return dateTime;

            return dateTime.Subtract(UTC_PLUS_7_OFFSET);
        }

        /// <summary>
        /// Converts UTC datetime to UTC+7 (for API response)
        /// </summary>
        public static DateTime ConvertToUtcPlus7(DateTime utcDateTime)
        {
            DateTime utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

            // Add 7 hours
            return utc.Add(UTC_PLUS_7_OFFSET);
        }

        /// <summary>
        /// Formats datetime to ISO 8601 format: yyyy-MM-ddTHH:mm:ss
        /// </summary>
        public static string ToIso8601String(DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        /// <summary>
        /// Converts UTC+7 nullable datetime to UTC
        /// </summary>
        public static DateTime? ConvertToUtc(DateTime? dateTime)
        {
            return dateTime.HasValue ? ConvertToUtc(dateTime.Value) : null;
        }

        /// <summary>
        /// Converts UTC nullable datetime to UTC+7
        /// </summary>
        public static DateTime? ConvertToUtcPlus7(DateTime? utcDateTime)
        {
            return utcDateTime.HasValue ? ConvertToUtcPlus7(utcDateTime.Value) : null;
        }

        /// <summary>
        /// Formats nullable datetime to ISO 8601 string
        /// </summary>
        public static string? ToIso8601String(DateTime? dateTime)
        {
            return dateTime.HasValue ? ToIso8601String(dateTime.Value) : null;
        }
    }
}
