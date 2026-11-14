using System.Net;
using System.Text.Json;
using DataControls.Api.Contracts;
using DataControls.Domain.Controls;
using DataControls.Tests.Integration;
using static DataControls.Tests.Support.Defs;

namespace DataControls.Tests.Api;

/// <summary>Creates controls through the API and removes them (with their runs) after the test class.</summary>
public abstract class ApiDbTestBase(ApiFactory factory) : IAsyncLifetime
{
    protected readonly HttpClient Client = factory.CreateClient();
    private readonly List<int> _created = [];

    public const string AllDeliveries = """{"version":1,"rootEntity":"Delivery","condition":{"type":"rule","field":"DeliveryId","operator":"gt","compareTo":{"kind":"value","value":0}}}""";

    protected static string UniqueName(string prefix = "בדיקת API") => $"__{prefix} {Guid.NewGuid():N}";

    protected static string SaveBody(string name, string definitionJson, string severity = "High", string? rowVersion = null,
        bool isActive = true) =>
        JsonSerializer.Serialize(new
        {
            name,
            description = "נוצר על ידי בדיקה אוטומטית",
            severity,
            source = "Manual",
            isActive,
            definition = JsonDocument.Parse(definitionJson).RootElement,
            rowVersion
        });

    protected async Task<ControlDto> CreateAsync(string definitionJson = AllDeliveries, string? name = null, bool isActive = true)
    {
        var response = await Client.PostJsonAsync("/api/controls", SaveBody(name ?? UniqueName(), definitionJson, isActive: isActive));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var control = (await response.ReadAsync<ControlDto>())!;
        _created.Add(control.Id);
        return control;
    }

    protected void Track(int controlId) => _created.Add(controlId);

    protected async Task<RunDto> RunAsync(int controlId, int? maxRows = null)
    {
        var response = await Client.PostAsync($"/api/controls/{controlId}/runs{(maxRows is null ? "" : $"?maxRows={maxRows}")}", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsync<RunDto>())!;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (IntegrationDb.SkipReason is not null) return;
        foreach (var id in _created) await IntegrationDb.DeleteControlAsync(id);
    }
}

[Trait("Category", "Integration")]
public class ControlsApiTests(ApiFactory factory) : ApiDbTestBase(factory), IClassFixture<ApiFactory>
{
    [IntegrationFact]
    public async Task Full_lifecycle_create_get_update_deactivate_activate_delete()
    {
        // create (root entity in other casing → stored with the catalog's canonical key)
        var name = UniqueName();
        var created = await Client.PostJsonAsync("/api/controls",
            SaveBody(name, Json("delivery", Rule("PackageWeightKg", "gt", Field("Drone.MaxPayloadKg")))));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var control = (await created.ReadAsync<ControlDto>())!;
        Track(control.Id);
        Assert.EndsWith($"/api/controls/{control.Id}", created.Headers.Location!.ToString());
        Assert.Equal("Delivery", control.RootEntity);
        Assert.Equal("משלוח", control.RootEntityDisplayName);
        Assert.Equal(ControlSeverity.High, control.Severity);
        Assert.Equal("gt", control.Definition.GetProperty("condition").GetProperty("operator").GetString());

        // get
        var fetched = (await (await Client.GetAsync($"/api/controls/{control.Id}")).ReadAsync<ControlDto>())!;
        Assert.Equal(name, fetched.Name);
        Assert.Equal(control.RowVersion, fetched.RowVersion);

        // update
        var newName = UniqueName("עודכן");
        var updated = await Client.PutJsonAsync($"/api/controls/{control.Id}",
            SaveBody(newName, Json("Drone", Rule("Status", "eq", Value("\"Maintenance\""))), "Low", fetched.RowVersion));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var afterUpdate = (await updated.ReadAsync<ControlDto>())!;
        Assert.Equal(newName, afterUpdate.Name);
        Assert.Equal("Drone", afterUpdate.RootEntity);
        Assert.Equal(ControlSeverity.Low, afterUpdate.Severity);
        Assert.NotEqual(fetched.RowVersion, afterUpdate.RowVersion);

        // a stale rowVersion is a conflict
        var stale = await Client.PutJsonAsync($"/api/controls/{control.Id}",
            SaveBody(newName, AllDeliveries, rowVersion: fetched.RowVersion));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var staleBody = await stale.JsonAsync();
        Assert.Equal("rowVersion", staleBody.GetProperty("path").GetString());
        Assert.Contains("עודכנה בינתיים", staleBody.GetProperty("detail").GetString());

        // deactivate / activate
        var deactivated = (await (await Client.PostAsync($"/api/controls/{control.Id}/deactivate", null)).ReadAsync<ControlDto>())!;
        Assert.False(deactivated.IsActive);
        var inactiveList = (await (await Client.GetAsync($"/api/controls?isActive=false&search={Uri.EscapeDataString(newName)}"))
            .ReadAsync<List<ControlSummaryDto>>())!;
        Assert.Equal(control.Id, Assert.Single(inactiveList).Id);
        Assert.True((await (await Client.PostAsync($"/api/controls/{control.Id}/activate", null)).ReadAsync<ControlDto>())!.IsActive);

        // delete (never run)
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/controls/{control.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/controls/{control.Id}")).StatusCode);
    }

    [IntegrationFact]
    public async Task Invalid_input_returns_all_hebrew_errors_with_paths_and_saves_nothing()
    {
        var name = "   ";
        var response = await Client.PostJsonAsync("/api/controls", SaveBody(name, Json("Delivery", Group("AND",
            Rule("PackageWeightKg", "contains", Value("\"5\"")),
            Rule("Drone.Hub.Nope", "eq", Value("1"))))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("הגדרת הבקרה אינה תקינה", (await response.JsonAsync()).GetProperty("title").GetString());
        var errors = await response.ValidationErrorsAsync();
        Assert.Contains(errors, e => e.Path == "name" && e.Message == "יש להזין שם לבקרה.");
        Assert.Contains(errors, e => e.Path == "condition.children[0].operator");
        Assert.Contains(errors, e => e.Path == "condition.children[1].field");
        Assert.All(errors, e => Assert.Matches("[א-ת]", e.Message));
    }

    [IntegrationFact]
    public async Task Validate_does_not_save()
    {
        var name = UniqueName();
        var response = await Client.PostJsonAsync("/api/controls/validate", $$"""{"name":"{{name}}","definition":{{AllDeliveries}}}""");

        Assert.True((await response.ReadAsync<DefinitionCheckDto>())!.IsValid);
        var list = (await (await Client.GetAsync($"/api/controls?search={Uri.EscapeDataString(name)}"))
            .ReadAsync<List<ControlSummaryDto>>())!;
        Assert.Empty(list);
    }

    [IntegrationFact]
    public async Task Duplicate_name_is_a_conflict_on_create_and_on_update()
    {
        var existing = await CreateAsync();
        var other = await CreateAsync();

        var create = await Client.PostJsonAsync("/api/controls", SaveBody(existing.Name, AllDeliveries));
        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        Assert.Equal("name", (await create.JsonAsync()).GetProperty("path").GetString());

        var update = await Client.PutJsonAsync($"/api/controls/{other.Id}",
            SaveBody($"  {existing.Name}  ", AllDeliveries, rowVersion: other.RowVersion)); // trimmed before the check
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Contains("כבר קיימת", (await update.JsonAsync()).GetProperty("detail").GetString());
    }

    [IntegrationFact]
    public async Task Update_without_or_with_a_broken_row_version_is_a_400()
    {
        var control = await CreateAsync();

        var missing = await Client.PutJsonAsync($"/api/controls/{control.Id}", SaveBody(control.Name, AllDeliveries));
        var broken = await Client.PutJsonAsync($"/api/controls/{control.Id}", SaveBody(control.Name, AllDeliveries, rowVersion: "%%%"));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("rowVersion", Assert.Single(await missing.ValidationErrorsAsync()).Path);
        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
    }

    [IntegrationFact]
    public async Task Missing_control_is_a_404_problem()
    {
        var response = await Client.GetAsync("/api/controls/987654");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("הבקרה 987654 לא נמצאה.", (await response.JsonAsync()).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PostAsync("/api/controls/987654/runs", null)).StatusCode);
    }

    [IntegrationFact]
    public async Task A_control_with_run_history_cannot_be_deleted_only_deactivated()
    {
        var control = await CreateAsync();
        await RunAsync(control.Id);

        var response = await Client.DeleteAsync($"/api/controls/{control.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("ניתן להשבית", (await response.JsonAsync()).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/controls/{control.Id}")).StatusCode);
    }

    [IntegrationFact]
    public async Task An_inactive_control_cannot_be_run()
    {
        var control = await CreateAsync(isActive: false);

        var response = await Client.PostAsync($"/api/controls/{control.Id}/runs", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("מושבתת", (await response.JsonAsync()).GetProperty("detail").GetString());
    }

    [IntegrationFact]
    public async Task List_shows_the_last_run_and_filters_by_entity_and_source()
    {
        var control = await CreateAsync();
        var run = await RunAsync(control.Id);

        var list = (await (await Client.GetAsync(
                $"/api/controls?rootEntity=Delivery&source=Manual&search={Uri.EscapeDataString(control.Name)}"))
            .ReadAsync<List<ControlSummaryDto>>())!;
        var item = Assert.Single(list);
        Assert.Equal(run.RunId, item.LastRun!.RunId);
        Assert.Equal(ControlRunStatus.Succeeded, item.LastRun.Status);
        Assert.Equal("משלוח", item.RootEntityDisplayName);

        var otherEntity = (await (await Client.GetAsync(
                $"/api/controls?rootEntity=Drone&search={Uri.EscapeDataString(control.Name)}"))
            .ReadAsync<List<ControlSummaryDto>>())!;
        Assert.Empty(otherEntity);
    }

    [IntegrationFact]
    public async Task Catalog_of_the_real_database_exposes_no_physical_names()
    {
        var physical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        physical.UnionWith(await IntegrationDb.QueryAsync<string>("SELECT DISTINCT SchemaName FROM meta.Entities;"));
        physical.UnionWith(await IntegrationDb.QueryAsync<string>("SELECT DISTINCT TableName FROM meta.Entities;"));
        physical.UnionWith(await IntegrationDb.QueryAsync<string>("SELECT DISTINCT SchemaName + N'.' + TableName FROM meta.Entities;"));

        await MetadataApiTests.AssertNoPhysicalNamesAsync(Client, physical);
    }
}
