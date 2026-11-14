using System.Text.Json;
using DataControls.Application.Ai;
using DataControls.Application.Binding;
using DataControls.Application.Catalog;
using DataControls.Application.Errors;
using DataControls.Application.Formatting;
using DataControls.Tests.Support;
using static DataControls.Tests.Support.Defs;

namespace DataControls.Tests.Unit;

/// <summary>AiSuggestionService with a fake generator on the in-memory catalog (no AI service, no database).</summary>
public class AiSuggestionServiceTests
{
    private static AiSuggestionService Service(FakeAiControlGenerator generator)
    {
        var metadata = new StaticMetadataProvider(Catalog);
        return new AiSuggestionService(generator, new BusinessCatalogService(metadata), metadata, new DefinitionBinder());
    }

    private static readonly string ActiveDroneInClosedHub = Json("Drone", Group("AND",
        Rule("Status", "eq", Value("\"Active\"")),
        Rule("Hub.IsActive", "eq", Value("false"))));

    // ------------------------------------------------------------------ valid suggestion

    [Fact]
    public async Task Valid_suggestion_is_bound_summarized_and_returned_for_approval()
    {
        var generator = FakeAiControlGenerator.Returning(ActiveDroneInClosedHub, "הסבר מה-AI", 0.9, "הנחה");

        var s = await Service(generator).SuggestAsync("  רחפן פעיל במוקד סגור  ");

        Assert.True(s.IsValid);
        Assert.Empty(s.ValidationErrors);
        Assert.Equal("Drone", s.RootEntityKey);
        Assert.Equal("רחפן", s.RootEntityDisplayName);
        Assert.Equal("יאותרו רחפנים שבהם סטטוס רחפן: בשירות וגם מוקד פעיל (מוקד): לא.", s.Summary);
        Assert.Equal("הסבר מה-AI", s.Explanation);
        Assert.Equal(0.9, s.Confidence);
        Assert.Equal(["הנחה"], s.Warnings);
        Assert.Equal("רחפן פעיל במוקד סגור", generator.LastText);
    }

    [Fact]
    public async Task Definition_is_returned_in_canonical_form()
    {
        var generator = FakeAiControlGenerator.Returning(
            """{ "rootEntity": "Delivery", "version": 1, "condition": { "operator": "gt", "type": "rule", "field": "PackageWeightKg", "compareTo": { "field": "Drone.MaxPayloadKg", "kind": "field" } } }""");

        var s = await Service(generator).SuggestAsync("משקל מעל כושר נשיאה");

        Assert.True(s.IsValid);
        Assert.Equal(
            """{"version":1,"rootEntity":"Delivery","condition":{"type":"rule","field":"PackageWeightKg","operator":"gt","compareTo":{"kind":"field","field":"Drone.MaxPayloadKg"}}}""",
            s.DefinitionJson);
        Assert.Equal("יאותרו משלוחים שבהם משקל חבילה גדול מ-כושר נשיאה (רחפן).", s.Summary);
    }

    [Fact]
    public async Task Confidence_is_clamped()
    {
        var s = await Service(FakeAiControlGenerator.Returning(ActiveDroneInClosedHub, confidence: 7)).SuggestAsync("x");
        Assert.Equal(1, s.Confidence);
    }

    // ------------------------------------------------------------------ invalid / missing definition

    [Fact]
    public async Task No_definition_passes_the_ai_warnings_through()
    {
        var generator = FakeAiControlGenerator.Returning(null, "לא הצלחתי", 0.5, "הבקשה לא זוהתה");

        var s = await Service(generator).SuggestAsync("תביא פיצה");

        Assert.False(s.IsValid);
        Assert.Null(s.DefinitionJson);
        Assert.Null(s.Summary);
        Assert.Equal(0, s.Confidence);
        Assert.Equal(["הבקשה לא זוהתה"], s.Warnings);
        Assert.Empty(s.ValidationErrors);
    }

    [Fact]
    public async Task Definition_that_fails_the_binder_is_returned_with_the_same_errors_as_manual_validation()
    {
        var json = Json("Delivery", Group("AND",
            Rule("Salary", "gt", Value("1")),
            Rule("Drone.Model", "gt", Value("\"X\""))));

        var s = await Service(FakeAiControlGenerator.Returning(json, confidence: 0.99)).SuggestAsync("x");

        Assert.False(s.IsValid);
        Assert.Equal(0, s.Confidence);
        Assert.Null(s.Summary);
        Assert.Equal(json, s.DefinitionJson); // as received, so the user can fix it in the builder
        Assert.Equal(["condition.children[0].field", "condition.children[1].operator"], s.ValidationErrors.Select(e => e.Path));
    }

    [Fact]
    public async Task Value_out_of_column_range_is_caught_by_the_binder()
    {
        // DECIMAL(6,2) → max 9999.99; the Python service cannot know physical column ranges
        var json = Json("Delivery", Rule("PackageWeightKg", "gt", Value("1000000")));
        var s = await Service(FakeAiControlGenerator.Returning(json)).SuggestAsync("x");

        Assert.False(s.IsValid);
        Assert.Single(s.ValidationErrors);
    }

    [Theory]
    [InlineData("""{"version":1,"rootEntity":"Delivery","condition":{"type":"sql","text":"DROP TABLE x"}}""")]
    [InlineData("""{"version":1,"rootEntity":"Delivery","condition":{"type":"rule","field":"DistanceKm","operator":"gt","compareTo":{"kind":"value","value":1}},"sql":"x"}""")]
    [InlineData("[1,2,3]")]
    [InlineData("not json")]
    public async Task Malformed_definition_becomes_a_validation_error(string json)
    {
        var s = await Service(FakeAiControlGenerator.Returning(json)).SuggestAsync("x");

        Assert.False(s.IsValid);
        Assert.Single(s.ValidationErrors);
        Assert.Equal(0, s.Confidence);
    }

    // ------------------------------------------------------------------ request / failures

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_text_is_rejected_without_calling_the_ai(string? text)
    {
        var generator = FakeAiControlGenerator.Returning(ActiveDroneInClosedHub);

        var ex = await Assert.ThrowsAsync<InvalidRequestException>(() => Service(generator).SuggestAsync(text));

        Assert.Equal("text", ex.Errors.Single().Path);
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task Too_long_text_is_rejected() =>
        await Assert.ThrowsAsync<InvalidRequestException>(() =>
            Service(FakeAiControlGenerator.Returning(null)).SuggestAsync(new string('א', AiSuggestionService.MaxTextLength + 1)));

    [Fact]
    public async Task Ai_unavailable_propagates()
    {
        var generator = new FakeAiControlGenerator { Throw = new AiServiceUnavailableException("down") };
        await Assert.ThrowsAsync<AiServiceUnavailableException>(() => Service(generator).SuggestAsync("x"));
    }

    [Fact]
    public void Service_cannot_save_or_run()
    {
        // structural guarantee: no repository, runner or execution service among the dependencies
        var parameterTypes = typeof(AiSuggestionService).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType.Name).ToList();

        Assert.Equal(["IAiControlGenerator", "IBusinessCatalogService", "IMetadataProvider", "DefinitionBinder"], parameterTypes);
    }

    // ------------------------------------------------------------------ catalog sent to the AI

    [Fact]
    public async Task Catalog_contains_business_paths_types_operators_and_options()
    {
        var catalog = await Service(new FakeAiControlGenerator()).BuildCatalogAsync();

        Assert.Equal(1, catalog.Version);
        Assert.Equal(5, catalog.Entities.Count);

        var delivery = Assert.Single(catalog.Entities, e => e.Key == "Delivery");
        Assert.Equal("משלוחים", delivery.DisplayNamePlural);
        var hubActive = Assert.Single(delivery.Fields, f => f.Path == "Drone.Hub.IsActive");
        Assert.Equal("boolean", hubActive.DataType);
        Assert.Equal("מוקד פעיל (רחפן › מוקד)", hubActive.DisplayName);
        var status = Assert.Single(delivery.Fields, f => f.Path == "Status");
        Assert.Equal("enum", status.DataType);
        Assert.Contains(status.Options, o => o.Value == "Delivered" && o.DisplayName == "נמסר");

        Assert.Contains(catalog.Operators, o => o.Key == "isEmpty" && !o.RequiresValue);
        Assert.Contains(catalog.TypeOperators, t => t is { DataType: "date", Operator: "lt", DisplayName: "לפני", AllowFieldCompare: true });
        Assert.DoesNotContain(catalog.TypeOperators, t => t is { DataType: "text", Operator: "gt" });
        Assert.All(catalog.TypeOperators, t => Assert.Contains(catalog.Operators, o => o.Key == t.Operator));
    }

    [Fact]
    public async Task Catalog_never_contains_physical_names()
    {
        var json = JsonSerializer.Serialize(await Service(new FakeAiControlGenerator()).BuildCatalogAsync());

        // (field keys equal column names in this catalog, so only table/schema names and meta columns are checked)
        foreach (var physical in new[] { "\"ops", "Deliveries", "Drones", "Hubs", "Pilots", "Customers", "SchemaName", "TableName", "ColumnName", "PrimaryKey" })
            Assert.DoesNotContain(physical, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prompt_preview_goes_through_the_generator_with_the_catalog()
    {
        var generator = new FakeAiControlGenerator();
        var preview = await Service(generator).PreviewPromptAsync(" משלוח באיחור ");

        Assert.Equal("user: משלוח באיחור", preview.User);
        Assert.Equal(5, generator.LastCatalog!.Entities.Count);
    }

    // ------------------------------------------------------------------ summary

    [Fact]
    public void Summary_of_nested_groups_uses_parentheses_only_next_to_siblings()
    {
        var bound = Bind(Json("Delivery", Group("OR",
            Group("AND", Rule("Status", "eq", Value("\"Delivered\"")), Rule("DeliveredAt", "isEmpty")),
            Group("AND", Rule("Status", "neq", Value("\"Failed\"")), Rule("DispatchedAt", "lt", Today(-1))))));

        Assert.Equal(
            "יאותרו משלוחים שבהם (סטטוס משלוח: נמסר וגם זמן מסירה ריק) או (סטטוס משלוח אינו נכשל וגם זמן יציאה לפני היום - 1 ימים).",
            DefinitionSummaryBuilder.Build(bound));
    }
}
