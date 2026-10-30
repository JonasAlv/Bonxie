using Newtonsoft.Json;

namespace Skua.Core.Models.Converters;

/// <summary>
/// Reads ints and longs written as whole-number floats ("815187199.0"), which some player builds emit,
/// instead of throwing. Registered globally in <see cref="JsonConvert.DefaultSettings"/> at startup.
/// </summary>
public class WholeNumberConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        Type type = Nullable.GetUnderlyingType(objectType) ?? objectType;
        return type == typeof(int) || type == typeof(long);
    }

    public override bool CanWrite => false;

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => throw new NotSupportedException();

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null || reader.Value is null or "")
            return Nullable.GetUnderlyingType(objectType) is null ? Activator.CreateInstance(objectType) : null;

        Type type = Nullable.GetUnderlyingType(objectType) ?? objectType;
        // Convert rounds, so a stray "123.0" reads as 123; strings like "42" still parse.
        return Convert.ChangeType(reader.Value, type, System.Globalization.CultureInfo.InvariantCulture);
    }
}
