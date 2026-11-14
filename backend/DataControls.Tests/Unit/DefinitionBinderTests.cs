using DataControls.Application.Binding;
using DataControls.Application.Errors;
using DataControls.Tests.Support;
using static DataControls.Tests.Support.Defs;

namespace DataControls.Tests.Unit;

public class DefinitionBinderTests
{
    private static ControlValidationException Invalid(string json) =>
        Assert.Throws<ControlValidationException>(() => Bind(json));

    private static void AssertSingleError(string json, string expectedFragment)
    {
        var ex = Invalid(json);
        var error = Assert.Single(ex.Errors);
        Assert.Contains(expectedFragment, error.Message);
    }

    // ---------------- resolution ----------------

    [Fact]
    public void Resolves_paths_through_relations_and_shares_joins()
    {
        var bound = Bind(Json("Delivery", Group("AND",
            Rule("Drone.Status", "eq", Value("\"Maintenance\"")),
            Rule("Drone.Hub.IsActive", "eq", Value("false")),
            Rule("PackageWeightKg", "gt", Field("Drone.MaxPayloadKg")))));

        Assert.Equal(["Drone", "Drone.Hub"], bound.Joins.Select(j => j.Path));
        Assert.Same(bound.Joins[0], bound.Joins[1].Parent);
        Assert.Equal(3, bound.Rules.Count);
        Assert.Equal("מוקד פעיל (רחפן › מוקד)", bound.Rules[1].Field.DisplayName);
    }

    [Fact]
    public void Keys_are_case_insensitive_and_canonicalized()
    {
        var bound = Bind(Json("delivery", Rule("drone.status", "EQ", Value("\"maintenance\""))));
        var rule = bound.Rules.Single();
        Assert.Equal("Drone.Status", rule.Field.Path);
        Assert.Equal("Maintenance", Assert.IsType<BoundValue>(rule.CompareTo).Value);
    }

    [Theory]
    [InlineData("NoSuchField", "השדה \"NoSuchField\" אינו קיים")]
    [InlineData("Drone.NoSuchField", "השדה \"NoSuchField\" אינו קיים בישות \"רחפן\"")]
    [InlineData("Hub.Name", "הקשר \"Hub\" אינו קיים")]   // Hub is reachable only via Drone
    [InlineData("Drone..Status", "אינו תקין")]
    [InlineData("A.B.C.D.E.F", "אינו תקין")]
    public void Unknown_paths_are_rejected(string path, string message) =>
        AssertSingleError(Json("Delivery", Rule(path, "isEmpty")), message);

    [Fact]
    public void Unknown_root_entity_is_rejected() =>
        AssertSingleError(Json("Spaceship", Rule("Id", "isEmpty")), "הישות \"Spaceship\" אינה קיימת");

    [Fact]
    public void Unsupported_version_is_rejected() =>
        AssertSingleError("""{"version":2,"rootEntity":"Customer","condition":{"type":"rule","field":"Phone","operator":"isEmpty"}}""", "גרסת הגדרה 2");

    // ---------------- operator vs type ----------------

    [Theory]
    [InlineData("Customer", "FullName", "gt", "\"x\"")]        // ">" on text
    [InlineData("Delivery", "PackageWeightKg", "contains", "\"5\"")]
    [InlineData("Customer", "Phone", "lt", "\"x\"")]
    [InlineData("Delivery", "Status", "gt", "\"Pending\"")]    // enum: eq/neq only
    public void Operator_must_be_valid_for_the_field_type(string root, string field, string op, string value) =>
        AssertSingleError(Json(root, Rule(field, op, Value(value))), "אינו מתאים לשדה");

    [Fact]
    public void Unknown_operator_is_rejected() =>
        AssertSingleError(Json("Customer", Rule("Phone", "like", Value("\"x\""))), "אופרטור לא מוכר");

    [Fact]
    public void Missing_compare_value_is_rejected() =>
        AssertSingleError(Json("Delivery", Rule("PackageWeightKg", "gt")), "חסר ערך להשוואה");

    [Fact]
    public void Value_on_isEmpty_is_rejected() =>
        AssertSingleError(Json("Customer", Rule("Phone", "isEmpty", Value("\"x\""))), "אינו מקבל ערך");

    [Fact]
    public void Field_compare_only_where_allowed() =>
        AssertSingleError(Json("Delivery", Rule("Pilot.IsActive", "eq", Field("Drone.Hub.IsActive"))), "לשדה אחר");

    [Fact]
    public void Field_compare_requires_compatible_types() =>
        AssertSingleError(Json("Delivery", Rule("PackageWeightKg", "gt", Field("DispatchedAt"))), "לא ניתן להשוות");

    [Fact]
    public void Date_and_datetime_are_comparable() =>
        Bind(Json("Delivery", Rule("Pilot.LicenseExpiry", "lt", Field("DispatchedAt"))));

    [Fact]
    public void Today_and_now_only_on_temporal_fields()
    {
        AssertSingleError(Json("Delivery", Rule("PackageWeightKg", "gt", Today())), "בשדה תאריך");
        AssertSingleError(Json("Delivery", Rule("PackageWeightKg", "gt", Now)), "בשדה תאריך");
    }

    // ---------------- values ----------------

    [Fact]
    public void Enum_value_must_be_a_known_option_and_lists_the_options()
    {
        var ex = Invalid(Json("Delivery", Rule("Status", "eq", Value("\"Lost\""))));
        Assert.Contains("ערכים אפשריים: ממתין, בדרך, נמסר, נכשל", Assert.Single(ex.Errors).Message);
    }

    [Theory]
    [InlineData("PackageWeightKg", "\"heavy\"", "יש להזין בו מספר")]
    [InlineData("PackageWeightKg", "true", "יש להזין בו מספר")]
    [InlineData("DispatchedAt", "\"yesterday\"", "תאריך ושעה")]
    [InlineData("Status", "null", "חסר ערך")]
    public void Values_must_match_the_field_type(string field, string value, string message) =>
        AssertSingleError(Json("Delivery", Rule(field, "eq", Value(value))), message);

    [Fact]
    public void Numeric_string_is_accepted() =>
        Assert.Equal(5m, Assert.IsType<BoundValue>(Bind(Json("Delivery", Rule("PackageWeightKg", "gt", Value("\"5\"")))).Rules[0].CompareTo).Value);

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public void Empty_text_value_points_to_isEmpty(string value) =>
        AssertSingleError(Json("Customer", Rule("City", "eq", Value(value))), "\"ריק\"");

    // ---------------- ranges (validation error in Hebrew, never a SQL error) ----------------

    [Theory]
    [InlineData(36_500)]
    [InlineData(-36_500)]
    [InlineData(0)]
    public void OffsetDays_within_range_is_accepted(int offset) =>
        Assert.IsType<BoundToday>(Bind(Json("Drone", Rule("NextMaintenanceDue", "lt", Today(offset)))).Rules[0].CompareTo);

    [Theory]
    [InlineData(36_501)]
    [InlineData(-36_501)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void OffsetDays_out_of_range_is_a_hebrew_validation_error(int offset)
    {
        var ex = Invalid(Json("Drone", Rule("NextMaintenanceDue", "lt", Today(offset))));
        var error = Assert.Single(ex.Errors);
        Assert.EndsWith(".offsetDays", error.Path);
        Assert.Contains("חורג מהטווח המותר (בין -36500 ל-36500)", error.Message);
    }

    [Theory]
    [InlineData("9999.99")]
    [InlineData("-9999.99")]
    [InlineData("0.001")]     // more decimals than the column scale is still a valid comparison
    public void Number_within_decimal_column_range_is_accepted(string value) =>
        Bind(Json("Delivery", Rule("PackageWeightKg", "gt", Value(value))));

    [Theory]
    [InlineData("PackageWeightKg", "10000", "בין -9999.99 ל-9999.99")]   // DECIMAL(6,2)
    [InlineData("PackageWeightKg", "-10000", "בין -9999.99 ל-9999.99")]
    [InlineData("DistanceKm", "\"123456\"", "בין -9999.99 ל-9999.99")]
    [InlineData("DeliveryId", "3000000000", "בין -2147483648 ל-2147483647")] // INT
    [InlineData("PackageWeightKg", "1e30", "חורג מהטווח המותר")]           // beyond System.Decimal
    public void Number_out_of_column_range_is_a_hebrew_validation_error(string field, string value, string message)
    {
        var ex = Invalid(Json("Delivery", Rule(field, "gt", Value(value))));
        var error = Assert.Single(ex.Errors);
        Assert.Contains("חורג מהטווח המותר", error.Message);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Text_value_longer_than_limit_is_rejected() =>
        AssertSingleError(Json("Customer", Rule("City", "contains", Value($"\"{new string('א', DefinitionLimits.MaxTextLength + 1)}\""))), "ארוך מדי");

    // ---------------- structure limits ----------------

    [Fact]
    public void Empty_group_is_rejected() =>
        AssertSingleError(Json("Customer", Group("AND")), "קבוצת תנאים ריקה");

    [Fact]
    public void Too_deep_nesting_is_rejected()
    {
        var node = Rule("Phone", "isEmpty");
        for (var i = 0; i < DefinitionLimits.MaxDepth + 1; i++) node = Group("AND", node);
        AssertSingleError(Json("Customer", node), "עמוק מדי");
    }

    [Fact]
    public void Too_many_rules_are_rejected()
    {
        var rules = Enumerable.Repeat(Rule("Phone", "isEmpty"), DefinitionLimits.MaxRules + 1).ToArray();
        AssertSingleError(Json("Customer", Group("OR", rules)), "יותר מדי תנאים");
    }

    [Fact]
    public void All_errors_are_reported_together_with_their_paths()
    {
        var ex = Invalid(Json("Delivery", Group("AND",
            Rule("NoSuchField", "isEmpty"),
            Rule("Status", "eq", Value("\"Lost\"")),
            Rule("DispatchedAt", "gt", Today(50_000)))));

        Assert.Equal(
            ["condition.children[0].field", "condition.children[1].compareTo.value", "condition.children[2].compareTo.offsetDays"],
            ex.Errors.Select(e => e.Path));
    }
}
