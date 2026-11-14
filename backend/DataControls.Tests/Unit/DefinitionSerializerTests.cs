using DataControls.Domain.Definition;
using DataControls.Tests.Support;

namespace DataControls.Tests.Unit;

public class DefinitionSerializerTests
{
    [Fact]
    public void Scripts_contain_14_sample_controls_and_14_expectations()
    {
        Assert.Equal(14, DatabaseScripts.SampleDefinitions().Count);
        Assert.Equal(14, DatabaseScripts.ExpectedViolations().Count);
    }

    [Theory]
    [MemberData(nameof(DatabaseScripts.ControlNumbers), MemberType = typeof(DatabaseScripts))]
    public void Every_sample_control_parses_binds_builds_and_round_trips(int controlNumber)
    {
        var json = DatabaseScripts.SampleDefinitions()[controlNumber];

        var definition = ControlDefinitionSerializer.Parse(json);
        var reparsed = ControlDefinitionSerializer.Parse(ControlDefinitionSerializer.Serialize(definition));

        Assert.Equal(ControlDefinitionSerializer.Serialize(definition), ControlDefinitionSerializer.Serialize(reparsed));
        Assert.NotEmpty(Defs.Build(json).Sql);
    }

    [Fact]
    public void Discriminator_may_appear_after_other_properties()
    {
        var definition = ControlDefinitionSerializer.Parse(
            """{"condition":{"field":"PackageWeightKg","operator":"lt","compareTo":{"value":0,"kind":"value"},"type":"rule"},"rootEntity":"Delivery","version":1}""");

        var rule = Assert.IsType<ConditionRule>(definition.Condition);
        Assert.IsType<ValueOperand>(rule.CompareTo);
    }

    [Fact]
    public void Group_logic_is_written_as_upper_case()
    {
        var json = Defs.Json("Customer", Defs.Group("and", Defs.Rule("Phone", "isEmpty")));
        var serialized = ControlDefinitionSerializer.Serialize(ControlDefinitionSerializer.Parse(json));
        Assert.Contains("\"logic\":\"AND\"", serialized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"version":1,"rootEntity":"Delivery","condition":{"field":"X","operator":"eq"}}""")]                    // no "type"
    [InlineData("""{"version":1,"rootEntity":"Delivery","condition":{"type":"sql","text":"DROP TABLE x"}}""")]            // unknown type
    [InlineData("""{"version":1,"rootEntity":"Delivery","sql":"SELECT 1","condition":{"type":"rule","field":"Status","operator":"isEmpty"}}""")] // unknown property
    [InlineData("""{"version":1,"rootEntity":"Delivery","condition":{"type":"rule","field":"DispatchedAt","operator":"gt","compareTo":{"kind":"today","offsetDays":99999999999}}}""")] // offset beyond int
    [InlineData("""{"version":1,"rootEntity":"Customer","condition":{"type":"group","logic":5,"children":[{"type":"rule","field":"Phone","operator":"isEmpty"}]}}""")] // numeric logic
    [InlineData("""{"version":1,"rootEntity":"Customer","condition":{"type":"group","logic":"XOR","children":[{"type":"rule","field":"Phone","operator":"isEmpty"}]}}""")]
    public void Malformed_definitions_raise_a_hebrew_format_error(string json)
    {
        var ex = Assert.Throws<DefinitionFormatException>(() => ControlDefinitionSerializer.Parse(json));
        Assert.Matches("[א-ת]", ex.Message);
    }
}
