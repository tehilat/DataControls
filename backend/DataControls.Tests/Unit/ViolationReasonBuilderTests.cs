using DataControls.Application.Results;
using DataControls.Tests.Support;
using static DataControls.Tests.Support.Defs;

namespace DataControls.Tests.Unit;

public class ViolationReasonBuilderTests
{
    private static string Reason(string definitionJson, params (string Key, object? Value)[] row) =>
        new ViolationReasonBuilder().Build(Bind(definitionJson), Catalog, row.ToDictionary(r => r.Key, r => r.Value));

    [Fact]
    public void Field_compare_shows_both_actual_values_with_units() =>
        Assert.Equal("משקל חבילה (12.5 ק\"ג) גדול מ-כושר נשיאה (רחפן) (10 ק\"ג)",
            Reason(DatabaseScripts.SampleDefinitions()[1], ("__f0", 12.50m), ("__g0", 10.00m), ("__r0", 1)));

    [Fact]
    public void Value_compare_uses_the_value_display() =>
        Assert.Equal("משקל חבילה (-2 ק\"ג) קטן מ-0 ק\"ג",
            Reason(DatabaseScripts.SampleDefinitions()[12], ("__f0", -2.00m), ("__r0", 1)));

    [Fact]
    public void And_group_explains_every_condition() =>
        Assert.Equal("טלפון ריק וגם אימייל ריק",
            Reason(DatabaseScripts.SampleDefinitions()[8], ("__f0", null), ("__r0", 1), ("__f1", "  "), ("__r1", 1)));

    [Fact]
    public void Or_group_explains_only_the_branch_that_held()
    {
        var reason = Reason(DatabaseScripts.SampleDefinitions()[10],
            ("__f0", "Failed"), ("__r0", 0), ("__f1", null), ("__r1", 1),
            ("__f2", "Failed"), ("__r2", 1), ("__f3", new DateTime(2026, 3, 1, 10, 15, 0)), ("__r3", 1));

        Assert.Equal("סטטוס משלוח: נכשל וגם זמן מסירה לא ריק (01/03/2026 10:15)", reason);
    }

    [Fact]
    public void Or_group_with_both_branches_true_parenthesizes_them()
    {
        var reason = Reason(Json("Delivery", Group("OR",
                Group("AND", Rule("Status", "eq", Value("\"Delivered\"")), Rule("DeliveredAt", "isEmpty")),
                Rule("PackageWeightKg", "lt", Value("0")))),
            ("__f0", "Delivered"), ("__r0", 1), ("__f1", null), ("__r1", 1), ("__f2", -1m), ("__r2", 1));

        Assert.Equal("(סטטוס משלוח: נמסר וגם זמן מסירה ריק) או משקל חבילה (-1 ק\"ג) קטן מ-0 ק\"ג", reason);
    }

    [Fact]
    public void Boolean_and_enum_show_the_actual_option()
    {
        Assert.Equal("מפעיל פעיל (מפעיל): לא",
            Reason(DatabaseScripts.SampleDefinitions()[14], ("__f0", false), ("__r0", 1)));
        Assert.Equal("סטטוס רחפן (רחפן): בתחזוקה",
            Reason(DatabaseScripts.SampleDefinitions()[13], ("__f0", "Maintenance"), ("__r0", 1)));
        Assert.Equal("סטטוס משלוח: נכשל (אינו נמסר)",
            Reason(Json("Delivery", Rule("Status", "neq", Value("\"Delivered\""))), ("__f0", "Failed"), ("__r0", 1)));
    }

    [Fact]
    public void Dates_relative_to_today_use_the_operator_override()
    {
        Assert.Equal("מועד תחזוקה הבא (05/01/2026) לפני היום",
            Reason(Json("Drone", Rule("NextMaintenanceDue", "lt", Today())), ("__f0", new DateTime(2026, 1, 5)), ("__r0", 1)));
        Assert.Equal("מועד תחזוקה הבא (05/01/2026) לפני היום + 30 ימים",
            Reason(Json("Drone", Rule("NextMaintenanceDue", "lt", Today(30))), ("__f0", new DateTime(2026, 1, 5)), ("__r0", 1)));
        Assert.Equal("זמן יציאה (05/01/2027 08:00) אחרי עכשיו",
            Reason(Json("Delivery", Rule("DispatchedAt", "gt", Now)), ("__f0", new DateTime(2027, 1, 5, 8, 0, 0)), ("__r0", 1)));
    }

    [Fact]
    public void Hebrew_text_is_quoted_and_whitespace_counts_as_empty()
    {
        Assert.Equal("עיר (\"חיפה\") שווה ל-\"חיפה\"",
            Reason(Json("Customer", Rule("City", "eq", Value("\"חיפה\""))), ("__f0", "חיפה"), ("__r0", 1)));
        Assert.Equal("כתובת ריק",
            Reason(Json("Customer", Rule("Address", "isEmpty")), ("__f0", "   "), ("__r0", 1)));
    }
}
