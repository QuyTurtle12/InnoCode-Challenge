using Microsoft.AspNetCore.Http;
using Utility.Constant;
using Utility.ExceptionCustom;

namespace Utility.Helpers
{
    public class CsvHelpers
    {
        public static void ValidateCsvFile(IFormFile file)
        {
            // Check if file is null or empty
            if (file == null || file.Length == 0)
            {
                throw new ErrorException(
                    StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "CSV file is required."
                );
            }

            // Check file extension and size
            string extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension != ".csv")
            {
                throw new ErrorException(
                    StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "Only CSV files (.csv) are allowed."
                );
            }

            // Limit file size to 5MB
            if (file.Length > 5 * 1024 * 1024)
            {
                throw new ErrorException(
                    StatusCodes.Status400BadRequest,
                    ResponseCodeConstants.BADREQUEST,
                    "File size must not exceed 5MB."
                );
            }
        }

        public static string ExtractBankNameFromCsv(string csvContent)
        {
            using StringReader reader = new StringReader(csvContent);
            string? firstLine = reader.ReadLine();

            if (string.IsNullOrEmpty(firstLine))
                return string.Empty;

            // Detect delimiter (comma or semicolon)
            char delimiter = DetectDelimiter(firstLine);

            // Check for BankName with detected delimiter
            if (firstLine.StartsWith($"BankName{delimiter}", StringComparison.OrdinalIgnoreCase))
            {
                // Split on detected delimiter
                string[]? parts = firstLine.Split(delimiter, 2);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    // Trim whitespace, quotes, AND trailing delimiters
                    return parts[1]
                        .Trim()
                        .Trim('"')
                        .TrimEnd(',', ';');
                }
            }

            return string.Empty;
        }

        public static char DetectDelimiter(string line)
        {
            if (string.IsNullOrEmpty(line))
                return ',';

            // Count occurrences
            int commaCount = line.Count(c => c == ',');
            int semicolonCount = line.Count(c => c == ';');

            // Return the more common delimiter
            return semicolonCount > commaCount ? ';' : ',';
        }
    }
}
