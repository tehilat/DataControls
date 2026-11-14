using System.Globalization;
using System.Text;
using System.Text.Json;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Results;

/// <summary>
/// Writes record values as JSON in one canonical, culture-independent format, so stored results
/// can be sorted and filtered by type in SQL (TRY_CONVERT) and read back unambiguously by the client:
/// number → JSON number (invariant), date → "yyyy-MM-dd", datetime → "yyyy-MM-ddTHH:mm:ss.fffffff",
/// boolean → true/false, text/enum → string (enum = raw option value), missing → null.
/// </summary>
public static class CanonicalValueJson
{
    public const string DateFormat = "yyyy-MM-dd";
    public const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffffff";

    public static string Serialize(IReadOnlyDictionary<string, object?> values, IReadOnlyList<ResultColumn> columns)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var column in columns)
            {
                writer.WritePropertyName(column.Key);
                WriteValue(writer, values.GetValueOrDefault(column.Key), column.DataType);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void WriteValue(Utf8JsonWriter writer, object? value, FieldDataType type)
    {
        if (value is null or DBNull)
        {
            writer.WriteNullValue();
            return;
        }

        switch (type)
        {
            case FieldDataType.Number:
                WriteNumber(writer, value);
                break;
            case FieldDataType.Date when TryGetDateTime(value, out var date):
                writer.WriteStringValue(date.ToString(DateFormat, CultureInfo.InvariantCulture));
                break;
            case FieldDataType.DateTime when TryGetDateTime(value, out var dateTime):
                writer.WriteStringValue(dateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture));
                break;
            case FieldDataType.Boolean when value is bool or byte or short or int or long:
                writer.WriteBooleanValue(Convert.ToBoolean(value, CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case decimal d: writer.WriteNumberValue(d); break;
            case int i: writer.WriteNumberValue(i); break;
            case long l: writer.WriteNumberValue(l); break;
            case short s: writer.WriteNumberValue(s); break;
            case byte b: writer.WriteNumberValue(b); break;
            case double or float:
                var dbl = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsFinite(dbl)) writer.WriteNumberValue(dbl);
                else writer.WriteNullValue();
                break;
            default:
                if (decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Number,
                        CultureInfo.InvariantCulture, out var parsed))
                    writer.WriteNumberValue(parsed);
                else
                    writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <remarks>A DateTimeOffset is stored by its local date and time (the offset is not kept).</remarks>
    private static bool TryGetDateTime(object value, out DateTime result)
    {
        switch (value)
        {
            case DateTime dt: result = dt; return true;
            case DateOnly d: result = d.ToDateTime(TimeOnly.MinValue); return true;
            case DateTimeOffset dto: result = dto.DateTime; return true;
            default: result = default; return false;
        }
    }
}
