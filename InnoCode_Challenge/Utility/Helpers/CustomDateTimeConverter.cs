using System.Text.Json;
using System.Text.Json.Serialization;

namespace Utility.Helpers
{
    public class CustomDateTimeConverter : JsonConverter<DateTime>
    {
        private const string DateFormat = "yyyy-MM-ddTHH:mm:ss";

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return DateTime.ParseExact(reader.GetString()!, DateFormat, null);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString(DateFormat));
        }
    }

    public class CustomNullableDateTimeConverter : JsonConverter<DateTime?>
    {
        private const string DateFormat = "yyyy-MM-ddTHH:mm:ss";

        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            return value == null ? null : DateTime.ParseExact(value, DateFormat, null);
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
                writer.WriteStringValue(value.Value.ToString(DateFormat));
            else
                writer.WriteNullValue();
        }
    }
}
