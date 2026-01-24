using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utility.Enums;

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

        /// <summary>
        /// Calculates date range based on predefined time range option
        /// </summary>
        public static (DateTime StartDate, DateTime EndDate) CalculateDateRange(
            TimeRangePredefinedEnum predefined)
        {
            DateTime now = DateTime.UtcNow;
            DateTime startDate;
            DateTime endDate;

            switch (predefined)
            {
                case TimeRangePredefinedEnum.CurrentMonth:
                    startDate = new DateTime(now.Year, now.Month, 1);
                    endDate = now;
                    break;

                case TimeRangePredefinedEnum.Last3Months:
                    startDate = new DateTime(now.Year, now.Month, 1).AddMonths(-2);
                    endDate = now;
                    break;

                case TimeRangePredefinedEnum.Last6Months:
                    startDate = new DateTime(now.Year, now.Month, 1).AddMonths(-5);
                    endDate = now;
                    break;

                case TimeRangePredefinedEnum.LastYear:
                    startDate = new DateTime(now.Year, now.Month, 1).AddMonths(-11);
                    endDate = now;
                    break;

                case TimeRangePredefinedEnum.AllTime:
                    startDate = new DateTime(2025, 1, 1);
                    endDate = DateTime.UtcNow;
                    break;
                default:
                    startDate = new DateTime(2025, 1, 1);
                    endDate = DateTime.UtcNow;
                    break;
            }

            return (startDate, endDate);
        }
    }
}
