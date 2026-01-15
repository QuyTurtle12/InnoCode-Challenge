using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Utility.Helpers
{
    public class CustomDateTimeConverter : JsonConverter<DateTime>
    {
        private const string DateFormatWithZ = "yyyy-MM-ddTHH:mm:ss'Z'";
        private const string DateFormatNoZone = "yyyy-MM-ddTHH:mm:ss";

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? dateString = reader.GetString();
            if (string.IsNullOrEmpty(dateString))
                throw new JsonException("Date string cannot be null or empty.");

            // Parse exact and preserve the value as UTC
            if (dateString.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            {
                DateTime parsed = DateTime.ParseExact(dateString, DateFormatWithZ, CultureInfo.InvariantCulture, DateTimeStyles.None);
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
            else
            {
                DateTime parsed = DateTime.ParseExact(dateString, DateFormatNoZone, CultureInfo.InvariantCulture, DateTimeStyles.None);
                // Treat values without zone as UTC as requested
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            DateTime utcValue = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
            writer.WriteStringValue(utcValue.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "Z");
        }
    }

    public class CustomNullableDateTimeConverter : JsonConverter<DateTime?>
    {
        private const string DateFormatWithZ = "yyyy-MM-ddTHH:mm:ss'Z'";
        private const string DateFormatNoZone = "yyyy-MM-ddTHH:mm:ss";

        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? dateString = reader.GetString();
            if (string.IsNullOrEmpty(dateString))
                return null;

            if (dateString.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            {
                DateTime parsed = DateTime.ParseExact(dateString, DateFormatWithZ, CultureInfo.InvariantCulture, DateTimeStyles.None);
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
            else
            {
                DateTime parsed = DateTime.ParseExact(dateString, DateFormatNoZone, CultureInfo.InvariantCulture, DateTimeStyles.None);
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (!value.HasValue)
            {
                writer.WriteNullValue();
                return;
            }

            DateTime v = value.Value;
            DateTime utcValue = v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc);
            writer.WriteStringValue(utcValue.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "Z");
        }
    }
}