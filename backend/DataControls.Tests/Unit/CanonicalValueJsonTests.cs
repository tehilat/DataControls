using System.Globalization;
using System.Text.Json;
using DataControls.Application.Results;
using DataControls.Domain.Metadata;

namespace DataControls.Tests.Unit;

public class CanonicalValueJsonTests
{
    private static readonly ResultColumn[] Columns =
    [
        new("Amount", "סכום", FieldDataType.Number, null),
        new("Count", "כמות", FieldDataType.Number, null),
        new("Day", "יום", FieldDataType.Date, null),
        new("At", "מועד", FieldDataType.DateTime, null),
        new("Flag", "דגל", FieldDataType.Boolean, null),
        new("Name", "שם", FieldDataType.Text, null),
        new("Status", "סטטוס", FieldDataType.Enum, null),
        new("Missing", "חסר", FieldDataType.Number, null)
    ];

    private static readonly Dictionary<string, object?> Values = new()
    {
        ["Amount"] = 1234.50m,
        ["Count"] = 7,
        ["Day"] = new DateTime(2026, 2, 1),
        ["At"] = new DateTime(2025, 12, 31, 23, 59, 59, 500),
        ["Flag"] = true,
        ["Name"] = "חיפה \"עיר\"",
        ["Status"] = "Delivered",
        ["Missing"] = DBNull.Value
    };

    [Theory]
    [InlineData("")]
    [InlineData("he-IL")]
    [InlineData("de-DE")]   // decimal comma
    [InlineData("ar-SA")]   // non-Gregorian calendar
    public void Output_does_not_depend_on_the_current_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(
                """{"Amount":1234.50,"Count":7,"Day":"2026-02-01","At":"2025-12-31T23:59:59.5000000","Flag":true,"Name":"חיפה \"עיר\"","Status":"Delivered","Missing":null}""",
                Unescape(CanonicalValueJson.Serialize(Values, Columns)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Values_round_trip_with_their_json_kinds()
    {
        using var document = JsonDocument.Parse(CanonicalValueJson.Serialize(Values, Columns));
        var root = document.RootElement;

        Assert.Equal(1234.5m, root.GetProperty("Amount").GetDecimal());
        Assert.Equal(JsonValueKind.True, root.GetProperty("Flag").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Missing").ValueKind);
        Assert.Equal("חיפה \"עיר\"", root.GetProperty("Name").GetString());
    }

    [Fact]
    public void Columns_without_a_value_are_written_as_null_and_unknown_keys_are_ignored()
    {
        var json = CanonicalValueJson.Serialize(new Dictionary<string, object?> { ["Other"] = 1 },
            [new ResultColumn("Amount", "סכום", FieldDataType.Number, null)]);

        Assert.Equal("""{"Amount":null}""", json);
    }

    [Fact]
    public void Date_column_drops_the_time_and_datetime_keeps_seven_fraction_digits()
    {
        var json = CanonicalValueJson.Serialize(
            new Dictionary<string, object?> { ["D"] = new DateTime(2026, 5, 6, 13, 14, 15), ["T"] = new DateOnly(2026, 5, 6) },
            [new ResultColumn("D", "d", FieldDataType.Date, null), new ResultColumn("T", "t", FieldDataType.DateTime, null)]);

        Assert.Equal("""{"D":"2026-05-06","T":"2026-05-06T00:00:00.0000000"}""", json);
    }

    private static string Unescape(string json) =>
        JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement,
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
