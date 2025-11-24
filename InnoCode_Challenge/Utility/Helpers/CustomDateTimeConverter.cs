using System.Text.Json;
using System.Text.Json.Serialization;

namespace Utility.Helpers
{
    public class CustomDateTimeConverter : JsonConverter<DateTime>
    {
        private const string DateFormat = "yyyy-MM-ddTHH:mm:ss'Z'";

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? dateString = reader.GetString();
            if (string.IsNullOrEmpty(dateString))
                throw new JsonException("Date string cannot be null or empty.");

            // Handle both formats: with Z and without Z
            if (dateString.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            {
                return DateTime.ParseExact(dateString, DateFormat, null, System.Globalization.DateTimeStyles.AssumeUniversal);
            }
            else
            {
                return DateTime.ParseExact(dateString, "yyyy-MM-ddTHH:mm:ss", null, System.Globalization.DateTimeStyles.AssumeUniversal);
            }
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            // Ensure the datetime is in UTC
            DateTime utcValue = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            writer.WriteStringValue(utcValue.ToString("yyyy-MM-ddTHH:mm:ss") + "Z");
        }
    }

    public class CustomNullableDateTimeConverter : JsonConverter<DateTime?>
    {
        private const string DateFormat = "yyyy-MM-ddTHH:mm:ss'Z'";

        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? dateString = reader.GetString();
            if (string.IsNullOrEmpty(dateString))
                return null;

            // Handle both formats: with Z and without Z
            if (dateString.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            {
                return DateTime.ParseExact(dateString, DateFormat, null, System.Globalization.DateTimeStyles.AssumeUniversal);
            }
            else
            {
                return DateTime.ParseExact(dateString, "yyyy-MM-ddTHH:mm:ss", null, System.Globalization.DateTimeStyles.AssumeUniversal);
            }
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                // Ensure the datetime is in UTC
                DateTime utcValue = value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime();
                writer.WriteStringValue(utcValue.ToString("yyyy-MM-ddTHH:mm:ss") + "Z");
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}