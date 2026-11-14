using System.Net;
using System.Text.Json;
using DataControls.Api.Contracts;
using DataControls.Tests.Support;

namespace DataControls.Tests.Api;

/// <summary>The catalog API on the in-memory test catalog (no database).</summary>
public class MetadataApiTests(CatalogOnlyApiFactory factory) : IClassFixture<CatalogOnlyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Entities_are_listed_in_business_terms()
    {
        var entities = await (await _client.GetAsync("/api/metadata/entities")).ReadAsync<List<EntityDto>>();

        Assert.NotNull(entities);
        Assert.Equal(5, entities.Count);
        var delivery = Assert.Single(entities, e => e.Key == "Delivery");
        Assert.Equal("משלוח", delivery.DisplayName);
        Assert.Equal("משלוחים", delivery.DisplayNamePlural);
    }

    [Fact]
    public async Task Fields_include_paths_through_relations_up_to_the_requested_depth()
    {
        var fields = await GetFields("Delivery", depth: 2);

        var own = Assert.Single(fields, f => f.Path == "PackageWeightKg");
        Assert.Empty(own.RelationPath);
        Assert.Equal("number", own.DataType);
        Assert.Equal("ק\"ג", own.Unit);

        var viaDrone = Assert.Single(fields, f => f.Path == "Drone.MaxPayloadKg");
        Assert.Equal(["רחפן"], viaDrone.RelationPath);
        Assert.Equal("כושר נשיאה (רחפן)", viaDrone.FullDisplayName);

        var viaHub = Assert.Single(fields, f => f.Path == "Drone.Hub.IsActive");
        Assert.Equal(["רחפן", "מוקד"], viaHub.RelationPath);
        Assert.Equal("boolean", viaHub.DataType);
        Assert.Equal("Hub", viaHub.EntityKey);

        Assert.Contains(fields, f => f.Path == "Customer.City");
        Assert.Contains(fields, f => f.Path == "Pilot.LicenseExpiry");
    }

    [Fact]
    public async Task Depth_limits_the_relations_that_are_followed()
    {
        var own = await GetFields("Delivery", depth: 0);
        var one = await GetFields("Delivery", depth: 1);

        Assert.All(own, f => Assert.DoesNotContain('.', f.Path));
        Assert.Contains(one, f => f.Path == "Drone.Model");
        Assert.DoesNotContain(one, f => f.Path.StartsWith("Drone.Hub.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enum_fields_carry_their_options()
    {
        var status = Assert.Single(await GetFields("Delivery", 1), f => f.Path == "Drone.Status");

        Assert.Equal("enum", status.DataType);
        Assert.Equal(["Active", "Maintenance", "Retired"], status.Options.Select(o => o.Value));
        Assert.Equal("בתחזוקה", status.Options[1].DisplayName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public async Task Depth_out_of_range_is_a_hebrew_400(int depth)
    {
        var response = await _client.GetAsync($"/api/metadata/entities/Delivery/fields?depth={depth}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (path, message) = Assert.Single(await response.ValidationErrorsAsync());
        Assert.Equal("depth", path);
        Assert.Contains("עומק", message);
    }

    [Fact]
    public async Task Unknown_entity_is_a_404_problem()
    {
        var response = await _client.GetAsync("/api/metadata/entities/Deliveries/fields");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("אינה קיימת", (await response.JsonAsync()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Operators_are_grouped_by_data_type_as_allowed_by_meta()
    {
        var groups = await (await _client.GetAsync("/api/metadata/operators")).ReadAsync<List<DataTypeOperatorsDto>>();
        Assert.NotNull(groups);

        var number = Assert.Single(groups, g => g.DataType == "number");
        var text = Assert.Single(groups, g => g.DataType == "text");
        var date = Assert.Single(groups, g => g.DataType == "date");
        var boolean = Assert.Single(groups, g => g.DataType == "boolean");

        Assert.Equal("מספר", number.DisplayName);
        Assert.Contains(number.Operators, o => o.Key == "gt" && o.AllowFieldCompare && o.RequiresValue);
        Assert.DoesNotContain(number.Operators, o => o.Key == "contains");
        Assert.Contains(text.Operators, o => o.Key == "contains" && !o.AllowFieldCompare);
        Assert.DoesNotContain(text.Operators, o => o.Key == "gt");
        Assert.Contains(text.Operators, o => o.Key == "isEmpty" && !o.RequiresValue);
        Assert.Equal("אחרי", Assert.Single(date.Operators, o => o.Key == "gt").DisplayName); // DisplayNameOverride
        Assert.Equal(["eq", "neq"], boolean.Operators.Select(o => o.Key));
    }

    [Fact]
    public async Task No_physical_names_are_exposed()
    {
        var rows = TestCatalog.Rows();
        var physical = rows.Entities.SelectMany(e => new[] { e.SchemaName, e.TableName })
            .Concat(rows.Columns.Select(c => c.TableName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await AssertNoPhysicalNamesAsync(_client, physical);
    }

    /// <summary>Walks every catalog endpoint (all entities, full depth) and checks property names and string values.</summary>
    internal static async Task AssertNoPhysicalNamesAsync(HttpClient client, IReadOnlySet<string> physicalNames)
    {
        var forbiddenProperties = new[]
        {
            "schemaName", "tableName", "columnName", "primaryKeyColumn", "displayColumn", "fromColumn", "toColumn",
            "sqlType", "generatedSql"
        };

        var documents = new List<JsonElement>
        {
            await (await client.GetAsync("/api/metadata/entities")).JsonAsync(),
            await (await client.GetAsync("/api/metadata/operators")).JsonAsync()
        };
        foreach (var entity in documents[0].EnumerateArray())
        {
            var key = entity.GetProperty("key").GetString();
            var response = await client.GetAsync($"/api/metadata/entities/{key}/fields?depth=4");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            documents.Add(await response.JsonAsync());
        }

        foreach (var document in documents)
            Walk(document);

        void Walk(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        Assert.DoesNotContain(property.Name, forbiddenProperties, StringComparer.OrdinalIgnoreCase);
                        Walk(property.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) Walk(item);
                    break;
                case JsonValueKind.String:
                    var value = element.GetString()!;
                    Assert.False(physicalNames.Contains(value), $"Physical name \"{value}\" was exposed by the catalog API.");
                    Assert.False(value.Contains('[') || value.Contains("ops.", StringComparison.OrdinalIgnoreCase),
                        $"Value \"{value}\" looks like SQL.");
                    break;
            }
        }
    }

    private async Task<List<FieldDto>> GetFields(string entity, int depth)
    {
        var response = await _client.GetAsync($"/api/metadata/entities/{entity}/fields?depth={depth}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadAsync<List<FieldDto>>())!;
    }
}

/// <summary>Relations that form cycles must not loop or repeat an entity on one path.</summary>
public class MetadataCycleApiTests(CyclicCatalogApiFactory factory) : IClassFixture<CyclicCatalogApiFactory>
{
    [Fact]
    public async Task An_entity_is_never_visited_twice_on_the_same_path()
    {
        var client = factory.CreateClient();
        var fields = (await (await client.GetAsync("/api/metadata/entities/Delivery/fields?depth=4"))
            .ReadAsync<List<FieldDto>>())!;

        Assert.Contains(fields, f => f.Path == "Drone.Hub.Name");
        Assert.DoesNotContain(fields, f => f.Path.StartsWith("Drone.Hub.AnyDrone.", StringComparison.Ordinal));
        Assert.DoesNotContain(fields, f => f.Path.StartsWith("Drone.Twin.", StringComparison.Ordinal));
        Assert.All(fields, f => Assert.Equal(f.RelationPath.Count, f.RelationPath.Distinct().Count()));

        // from Hub, the back relation to Drone is followed once
        var hubFields = (await (await client.GetAsync("/api/metadata/entities/Hub/fields?depth=4"))
            .ReadAsync<List<FieldDto>>())!;
        Assert.Contains(hubFields, f => f.Path == "AnyDrone.Model");
        Assert.DoesNotContain(hubFields, f => f.Path.StartsWith("AnyDrone.Hub.", StringComparison.Ordinal));
    }
}
