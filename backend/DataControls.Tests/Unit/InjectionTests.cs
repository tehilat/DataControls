using DataControls.Application.Binding;
using DataControls.Application.Errors;
using DataControls.Domain.Definition;
using DataControls.Domain.Metadata;
using DataControls.Infrastructure.Metadata;
using DataControls.Infrastructure.Querying;
using DataControls.Infrastructure.Sql;
using DataControls.Tests.Support;
using static DataControls.Tests.Support.Defs;

namespace DataControls.Tests.Unit;

/// <summary>Injection attempts at every entry point: values, paths, operators, entity keys and metadata itself.</summary>
public class InjectionTests
{
    private const string Payload = "'; DROP TABLE ops.Deliveries; --";

    [Theory]
    [InlineData("eq")]
    [InlineData("neq")]
    [InlineData("contains")]
    public void Malicious_text_values_only_ever_travel_as_parameters(string op)
    {
        var json = Json("Customer", Rule("FullName", op, Value(System.Text.Json.JsonSerializer.Serialize(Payload))));
        var q = Build(json);

        Assert.DoesNotContain("DROP", q.Sql);
        Assert.DoesNotContain("--", q.Sql);
        Assert.Contains(q.Parameters, p => p.Value is string s && s.Contains(Payload));
    }

    [Fact]
    public void Hebrew_payload_with_quotes_is_a_parameter()
    {
        var q = Build(Json("Customer", Rule("City", "eq", Value("\"ת'א'; DELETE FROM ops.Customers --\""))));
        Assert.DoesNotContain("DELETE", q.Sql);
        Assert.Contains("WHERE t0.[City] = @p0", q.Sql);
    }

    [Theory]
    [InlineData("PackageWeightKg; DROP TABLE ops.Deliveries")]
    [InlineData("PackageWeightKg]; DROP TABLE x; --")]
    [InlineData("ops.Deliveries.DeliveryId")]
    [InlineData("Drone].[MaxPayloadKg")]
    [InlineData("1=1 OR PackageWeightKg")]
    [InlineData("(SELECT TOP 1 Name FROM ctl.Controls)")]
    [InlineData("DeliveryId/**/")]
    public void Malicious_field_paths_are_rejected(string path)
    {
        Assert.Throws<ControlValidationException>(() => Bind(Json("Delivery", Rule(path, "isEmpty"))));
        Assert.Throws<ControlValidationException>(() =>
            Bind(Json("Delivery", Rule("PackageWeightKg", "gt", Field(path)))));
    }

    [Theory]
    [InlineData("gt OR 1=1")]
    [InlineData("=")]
    [InlineData("> 0; DROP TABLE x; --")]
    [InlineData("LIKE")]
    public void Malicious_operators_are_rejected(string op) =>
        Assert.Throws<ControlValidationException>(() => Bind(Json("Delivery", Rule("PackageWeightKg", op, Value("1")))));

    [Theory]
    [InlineData("ops.Deliveries")]
    [InlineData("Deliveries")]
    [InlineData("Delivery; DROP TABLE x")]
    [InlineData("ctl.Controls")]
    public void Malicious_root_entities_are_rejected(string root) =>
        Assert.Throws<ControlValidationException>(() => Bind(Json(root, Rule("DeliveryId", "isEmpty"))));

    [Theory]
    [InlineData("Status", "\"Delivered' OR '1'='1\"")]
    [InlineData("PackageWeightKg", "\"5 OR 1=1\"")]
    [InlineData("DispatchedAt", "\"2026-01-01'; DROP TABLE x; --\"")]
    [InlineData("PackageWeightKg", "{\"sql\":\"1\"}")]
    public void Malicious_typed_values_are_rejected(string field, string value) =>
        Assert.Throws<ControlValidationException>(() => Bind(Json("Delivery", Rule(field, "eq", Value(value)))));

    [Fact]
    public void Metadata_with_unsafe_or_nonexistent_identifiers_is_dropped()
    {
        var rows = TestCatalog.Rows(
            extraEntities:
            [
                TestCatalog.E("Evil", "רשע", "רשעים", "Deliveries; DROP TABLE x", "DeliveryId", "DeliveryId"),
                TestCatalog.E("OtherSchema", "אחר", "אחרים", "Controls", "ControlId", "ControlId", schema: "ctl"),
                TestCatalog.E("Ghost", "רפאים", "רפאים", "NoSuchTable", "Id", "Id")
            ],
            extraFields:
            [
                new FieldRow { FieldId = 900, EntityKey = "Delivery", FieldKey = "Bad", DisplayName = "רע",
                               ColumnName = "x]; DROP TABLE y; --", DataType = "text" },
                new FieldRow { FieldId = 901, EntityKey = "Delivery", FieldKey = "Missing", DisplayName = "חסר",
                               ColumnName = "NoSuchColumn", DataType = "text" }
            ],
            extraRelations: [TestCatalog.R("Delivery", "Sneaky", "Drone", "DroneId", "DroneId) OR (1=1")]);

        var catalog = MetadataCatalogFactory.Create(rows, ["ops"], out var warnings);

        Assert.False(catalog.TryGetEntity("Evil", out _));
        Assert.False(catalog.TryGetEntity("OtherSchema", out _));
        Assert.False(catalog.TryGetEntity("Ghost", out _));
        Assert.False(catalog.TryGetField("Delivery", "Bad", out _));
        Assert.False(catalog.TryGetField("Delivery", "Missing", out _));
        Assert.False(catalog.TryGetRelation("Delivery", "Sneaky", out _));
        Assert.Equal(6, warnings.Count);
        Assert.True(catalog.TryGetEntity("Delivery", out _)); // the good ones survive
    }

    [Fact]
    public void Query_builder_refuses_unsafe_identifiers_even_if_metadata_was_bypassed()
    {
        var evilField = new FieldMeta(1, "Delivery", "X", "x", null, "x]; DROP TABLE y; --",
            FieldDataType.Text, null, false, 0, ColumnInfo.Unknown);
        Assert.True(Catalog.TryGetEntity("Delivery", out var root));

        var definition = new BoundDefinition(
            root,
            new BoundRule(0, new BoundField("X", evilField, null, "x"), ComparisonOperator.IsEmpty, "ריק", null),
            [],
            [new BoundRule(0, new BoundField("X", evilField, null, "x"), ComparisonOperator.IsEmpty, "ריק", null)],
            []);

        Assert.Throws<InvalidOperationException>(() => new SqlQueryBuilder().Build(definition, 10));
    }

    [Theory]
    [InlineData("Valid_Name1", true)]
    [InlineData("_x", true)]
    [InlineData("1abc", false)]
    [InlineData("a b", false)]
    [InlineData("a]b", false)]
    [InlineData("a;b", false)]
    [InlineData("שם", false)]
    [InlineData("", false)]
    public void Identifier_whitelist_pattern(string name, bool valid) =>
        Assert.Equal(valid, SqlIdentifier.IsValid(name));
}
