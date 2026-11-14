using System.Globalization;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Formatting;

/// <summary>Formats values for Hebrew, user-facing text (reasons and validation messages).</summary>
public static class ValueFormatter
{
    public const string EmptyText = "ריק";

    public static string Format(object? value, FieldMeta field, MetadataCatalog catalog)
    {
        if (value is null or DBNull) return EmptyText;

        return field.DataType switch
        {
            FieldDataType.Number => WithUnit(FormatNumber(Convert.ToDecimal(value, CultureInfo.InvariantCulture)), field.Unit),
            FieldDataType.Text => value is string s && string.IsNullOrWhiteSpace(s) ? EmptyText : $"\"{value}\"",
            FieldDataType.Date => FormatDate(value),
            FieldDataType.DateTime => FormatDateTime(value),
            FieldDataType.Boolean => FormatBoolean(value),
            FieldDataType.Enum => catalog.GetOptions(field.FieldId)
                                      .FirstOrDefault(o => string.Equals(o.OptionValue, value.ToString(), StringComparison.OrdinalIgnoreCase))
                                      ?.DisplayName ?? value.ToString() ?? EmptyText,
            _ => value.ToString() ?? EmptyText
        };
    }

    public static string FormatNumber(decimal value) =>
        value.ToString("0.##########", CultureInfo.InvariantCulture);

    public static string WithUnit(string text, string? unit) =>
        string.IsNullOrWhiteSpace(unit) ? text : $"{text} {unit}";

    public static string FormatToday(int offsetDays) => offsetDays switch
    {
        0 => "היום",
        > 0 => $"היום + {offsetDays} ימים",
        _ => $"היום - {-offsetDays} ימים"
    };

    public const string NowText = "עכשיו";

    private static string FormatDate(object value) => value switch
    {
        DateTime d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? EmptyText
    };

    private static string FormatDateTime(object value) => value switch
    {
        DateTime d => d.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? EmptyText
    };

    private static string FormatBoolean(object value) => value switch
    {
        bool b => b ? "כן" : "לא",
        _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? "כן" : "לא"
    };
}
