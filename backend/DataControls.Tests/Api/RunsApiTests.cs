using System.Globalization;
using System.Net;
using System.Text.Json;
using DataControls.Api.Contracts;
using DataControls.Application.Results;
using DataControls.Application.Runs;
using DataControls.Domain.Controls;
using DataControls.Domain.Metadata;
using DataControls.Infrastructure.Persistence;
using DataControls.Tests.Integration;
using DataControls.Tests.Support;
using static DataControls.Tests.Support.Defs;

namespace DataControls.Tests.Api;

[Trait("Category", "Integration")]
public class RunsApiTests(ApiFactory factory) : ApiDbTestBase(factory), IClassFixture<ApiFactory>
{
    private async Task<PageDto<ViolationDto>> ViolationsAsync(int runId, string query = "")
    {
        var response = await Client.GetAsync($"/api/runs/{runId}/violations{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadAsync<PageDto<ViolationDto>>())!;
    }

    private static int DeliveryCount() =>
        IntegrationDb.QueryAsync<int>("SELECT COUNT(*) FROM ops.Deliveries;").Result.Single();

    // ---------------- running sample controls ----------------

    [IntegrationTheory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(12)]
    public async Task Running_a_sample_control_stores_exactly_the_expected_violations(int controlNumber)
    {
        var expected = DatabaseScripts.ExpectedViolations()[controlNumber];

        var run = await RunAsync(controlNumber);
        var page = await ViolationsAsync(run.RunId, "?pageSize=200");

        Assert.Equal(ControlRunStatus.Succeeded, run.Status);
        Assert.Equal(expected.Length, run.ViolationCount);
        Assert.Equal(expected.Length, run.StoredCount);
        Assert.False(run.Truncated);
        Assert.NotEmpty(run.Columns);
        Assert.Equal(expected, page.Items.Select(v => int.Parse(v.RecordId!, CultureInfo.InvariantCulture)).Order());
        Assert.All(page.Items, v => Assert.Matches("[א-ת]", v.Reason));
    }

    [IntegrationFact]
    public async Task Run_resource_has_a_location_and_never_exposes_the_generated_sql()
    {
        var response = await Client.PostAsync("/api/controls/1/runs", null);
        var runId = (await response.JsonAsync()).GetProperty("runId").GetInt32();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/api/runs/{runId}", response.Headers.Location!.ToString());

        foreach (var url in new[] { $"/api/runs/{runId}", "/api/runs?controlId=1", $"/api/runs/{runId}/violations" })
        {
            var text = await (await Client.GetAsync(url)).Content.ReadAsStringAsync();
            Assert.DoesNotContain("generatedSql", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SELECT", text);
            Assert.DoesNotContain("ops.", text);
        }
    }

    // ---------------- history ----------------

    [IntegrationFact]
    public async Task History_lists_runs_newest_first_and_filters_by_control_and_status()
    {
        var control = await CreateAsync();
        var first = await RunAsync(control.Id);
        var second = await RunAsync(control.Id);

        var history = (await (await Client.GetAsync($"/api/runs?controlId={control.Id}&status=Succeeded"))
            .ReadAsync<PageDto<RunListItemDto>>())!;

        Assert.Equal(2, history.TotalCount);
        Assert.Equal([second.RunId, first.RunId], history.Items.Select(r => r.RunId));
        Assert.All(history.Items, r => Assert.Equal(control.Name, r.ControlName));

        var failed = (await (await Client.GetAsync($"/api/runs?controlId={control.Id}&status=Failed"))
            .ReadAsync<PageDto<RunListItemDto>>())!;
        Assert.Equal(0, failed.TotalCount);

        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/runs/987654")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync("/api/runs?pageSize=500")).StatusCode);
    }

    [IntegrationFact]
    public async Task Row_limit_is_reported_as_truncated()
    {
        var control = await CreateAsync();

        var run = await RunAsync(control.Id, maxRows: 3);

        Assert.Equal(DeliveryCount(), run.ViolationCount);
        Assert.Equal(3, run.StoredCount);
        Assert.True(run.Truncated);
        Assert.Equal(3, (await ViolationsAsync(run.RunId)).TotalCount);
    }

    // ---------------- paging, search, filter ----------------

    [IntegrationFact]
    public async Task Pages_do_not_overlap_and_cover_every_record()
    {
        var control = await CreateAsync();
        var run = await RunAsync(control.Id);
        var total = DeliveryCount();

        var pages = new List<PageDto<ViolationDto>>();
        for (var page = 1; page <= (total + 4) / 5; page++)
            pages.Add(await ViolationsAsync(run.RunId, $"?page={page}&pageSize=5"));

        var rows = pages.SelectMany(p => p.Items).Select(v => v.RowNumber).ToList();
        Assert.All(pages, p => Assert.Equal(total, p.TotalCount));
        Assert.Equal((total + 4) / 5, pages[0].TotalPages);
        Assert.Equal(total, rows.Count);
        Assert.Equal(rows.Count, rows.Distinct().Count());
        Assert.Empty((await ViolationsAsync(run.RunId, $"?page={pages.Count + 1}&pageSize=5")).Items);
    }

    [IntegrationFact]
    public async Task Search_finds_hebrew_display_values_and_filter_matches_raw_values()
    {
        var control = await CreateAsync();
        var run = await RunAsync(control.Id);
        var delivered = (await IntegrationDb.QueryAsync<int>(
            "SELECT COUNT(*) FROM ops.Deliveries WHERE Status = N'Delivered';")).Single();
        Assert.True(delivered > 0);

        // the list value "Delivered" is shown as "נמסר" — search works in the user's terms
        var searched = await ViolationsAsync(run.RunId, $"?pageSize=200&search={Uri.EscapeDataString("נמסר")}");
        var filtered = await ViolationsAsync(run.RunId, "?pageSize=200&filter[Status]=Delivered");

        Assert.Equal(delivered, searched.TotalCount);
        Assert.Equal(delivered, filtered.TotalCount);
        Assert.All(filtered.Items, v => Assert.Equal("Delivered", v.Values.GetProperty("Status").GetString()));

        var byId = await ViolationsAsync(run.RunId, "?filter[DeliveryId]=3");
        Assert.Equal("3", Assert.Single(byId.Items).RecordId);
    }

    [IntegrationFact]
    public async Task Invalid_filter_sort_and_paging_are_hebrew_400s_with_paths()
    {
        var control = await CreateAsync();
        var run = await RunAsync(control.Id);

        var paging = await Client.GetAsync($"/api/runs/{run.RunId}/violations?page=0&pageSize=201");
        Assert.Equal(HttpStatusCode.BadRequest, paging.StatusCode);
        Assert.Equal(["page", "pageSize"], (await paging.ValidationErrorsAsync()).Select(e => e.Path));

        // every problem of the query is reported at once
        var response = await Client.GetAsync(
            $"/api/runs/{run.RunId}/violations?filter[Nope]=1&filter[PackageWeightKg]=abc&filter[DispatchedAt]=5/1/2026&sortBy=Drone.Model&sortDir=up");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await response.ValidationErrorsAsync();
        Assert.Contains(errors, e => e.Path == "filter[Nope]");
        Assert.Contains(errors, e => e.Path == "filter[PackageWeightKg]" && e.Message.Contains("מספר"));
        Assert.Contains(errors, e => e.Path == "filter[DispatchedAt]" && e.Message.Contains("yyyy-MM-dd"));
        Assert.Contains(errors, e => e.Path == "sortBy");
        Assert.Contains(errors, e => e.Path == "sortDir");
    }

    [IntegrationTheory]
    [InlineData("?search=%27%3B%20DROP%20TABLE%20ctl.Controls%3B%20--")]
    [InlineData("?search=%25")]
    [InlineData("?search=%5B")]
    [InlineData("?search=_")]
    [InlineData("?filter[Status]=%27%20OR%201%3D1%20--")]
    [InlineData("?filter[Status]=%25")]
    public async Task Injection_attempts_in_search_and_filter_match_nothing(string query)
    {
        var control = await CreateAsync();
        var run = await RunAsync(control.Id);
        var deliveries = DeliveryCount();

        var page = await ViolationsAsync(run.RunId, query);

        Assert.Equal(0, page.TotalCount);
        Assert.Equal(deliveries, DeliveryCount());
        Assert.NotEmpty(await IntegrationDb.QueryAsync<int>("SELECT COUNT(*) FROM ctl.Controls;"));
    }

    [IntegrationTheory]
    [InlineData("?sortBy=DeliveryId%3B%20DROP%20TABLE%20ctl.Controls")]
    [InlineData("?sortBy=DeliveryId&sortDir=desc%3B%20DROP%20TABLE%20ctl.Controls")]
    [InlineData("?filter[DeliveryId%22%5D%27)%20OR%201%3D1--]=1")]
    public async Task Injection_attempts_in_keys_are_rejected(string query)
    {
        var control = await CreateAsync();
        var run = await RunAsync(control.Id);

        var response = await Client.GetAsync($"/api/runs/{run.RunId}/violations{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(await IntegrationDb.QueryAsync<int>("SELECT COUNT(*) FROM ctl.Controls;"));
    }

    // ---------------- typed sorting and filtering (deterministic synthetic run) ----------------

    private static readonly ResultColumn[] SyntheticColumns =
    [
        new("Amount", "סכום", FieldDataType.Number, null),
        new("Day", "יום", FieldDataType.Date, null),
        new("At", "מועד", FieldDataType.DateTime, null),
        new("Code", "קוד", FieldDataType.Text, null),
        new("Flag", "דגל", FieldDataType.Boolean, null)
    ];

    /// <summary>Row → (Amount, Day, At, Code, Flag). Text sorting of Amount would give 10, 100, 2.5, 9.</summary>
    private static readonly (decimal? Amount, DateTime? Day, DateTime? At, string Code, bool Flag)[] SyntheticRows =
    [
        (9m,      new DateTime(2026, 2, 1),   new DateTime(2026, 1, 1, 10, 0, 0),             "9",   true),
        (10.00m,  new DateTime(2025, 12, 31), new DateTime(2025, 12, 31, 23, 59, 59, 500),   "10",  false),
        (100m,    new DateTime(2026, 1, 15),  new DateTime(2026, 1, 1, 9, 0, 0),              "100", true),
        (2.5m,    new DateTime(2024, 6, 30),  new DateTime(2027, 3, 1, 0, 0, 0),              "2.5", false),
        (null,    null,                       null,                                           "x",   false)
    ];

    private async Task<int> SyntheticRunAsync()
    {
        var control = await CreateAsync();
        var repository = new SqlControlRunRepository(IntegrationDb.Connections);
        var runId = await repository.StartAsync(control.Id, "-- synthetic test run");

        var stored = SyntheticRows.Select((r, i) =>
        {
            var values = new Dictionary<string, object?>
            {
                ["Amount"] = r.Amount, ["Day"] = r.Day, ["At"] = r.At, ["Code"] = r.Code, ["Flag"] = r.Flag
            };
            return new StoredViolation(i + 1, $"R{i + 1}", $"רשומה {i + 1}",
                CanonicalValueJson.Serialize(values, SyntheticColumns), "סיבה לבדיקה", $"רשומה {i + 1}");
        }).ToList();

        await repository.SaveResultsAsync(runId, SyntheticColumns, stored);
        await repository.CompleteAsync(runId, ControlRunStatus.Succeeded, stored.Count, 0, null);
        return runId;
    }

    private async Task<string[]> SortedIds(int runId, string sortBy, string sortDir = "asc") =>
        (await ViolationsAsync(runId, $"?sortBy={sortBy}&sortDir={sortDir}")).Items.Select(v => v.RecordId!).ToArray();

    [IntegrationFact]
    public async Task Numbers_are_sorted_by_value_not_as_text()
    {
        var runId = await SyntheticRunAsync();

        // NULL first (SQL Server), then 2.5 < 9 < 10 < 100
        Assert.Equal(["R5", "R4", "R1", "R2", "R3"], await SortedIds(runId, "Amount"));
        Assert.Equal(["R3", "R2", "R1", "R4", "R5"], await SortedIds(runId, "amount", "DESC")); // key/dir case-insensitive

        // the same values in a text column sort as text: "10" < "100" < "2.5" < "9" < "x"
        Assert.Equal(["R2", "R3", "R4", "R1", "R5"], await SortedIds(runId, "Code"));
    }

    [IntegrationFact]
    public async Task Dates_and_datetimes_are_sorted_chronologically()
    {
        var runId = await SyntheticRunAsync();

        Assert.Equal(["R5", "R4", "R2", "R3", "R1"], await SortedIds(runId, "Day"));
        Assert.Equal(["R5", "R2", "R3", "R1", "R4"], await SortedIds(runId, "At"));
        Assert.Equal(["R4", "R1", "R3", "R2", "R5"], await SortedIds(runId, "At", "desc"));
    }

    [IntegrationTheory]
    [InlineData("filter[Amount]=10", "R2")]          // stored as 10.00 — equal by value
    [InlineData("filter[Amount]=10.000", "R2")]
    [InlineData("filter[Amount]=2.5", "R4")]
    [InlineData("filter[Day]=2026-02-01", "R1")]
    [InlineData("filter[At]=2026-01-01T09:00:00", "R3")]
    [InlineData("filter[At]=2025-12-31T23:59:59.5", "R2")]
    [InlineData("filter[Code]=100", "R3")]
    [InlineData("filter[Flag]=true&filter[Amount]=100", "R3")]
    public async Task Filters_compare_typed_values(string filter, string expectedId)
    {
        var runId = await SyntheticRunAsync();

        var page = await ViolationsAsync(runId, "?" + filter);

        Assert.Equal(expectedId, Assert.Single(page.Items).RecordId);
    }

    [IntegrationFact]
    public async Task Stored_values_are_canonical()
    {
        var runId = await SyntheticRunAsync();

        var page = await ViolationsAsync(runId, "?sortBy=Amount");
        var r2 = page.Items.Single(v => v.RecordId == "R2").Values;
        var r5 = page.Items.Single(v => v.RecordId == "R5").Values;

        Assert.Equal(JsonValueKind.Number, r2.GetProperty("Amount").ValueKind);
        Assert.Equal(10m, r2.GetProperty("Amount").GetDecimal());
        Assert.Equal("2025-12-31", r2.GetProperty("Day").GetString());
        Assert.Equal("2025-12-31T23:59:59.5000000", r2.GetProperty("At").GetString());
        Assert.Equal(JsonValueKind.False, r2.GetProperty("Flag").ValueKind);
        Assert.Equal(JsonValueKind.Null, r5.GetProperty("Amount").ValueKind);
    }

    [IntegrationTheory]
    [InlineData("filter[Amount]=abc", "מספר")]
    [InlineData("filter[Amount]=1,5", "מספר")]
    [InlineData("filter[Day]=5/1/2026", "yyyy-MM-dd")]
    [InlineData("filter[Day]=2026-13-01", "yyyy-MM-dd")]
    [InlineData("filter[At]=yesterday", "yyyy-MM-ddTHH:mm:ss")]
    [InlineData("filter[Flag]=yes", "true")]
    public async Task Filter_values_of_the_wrong_type_are_rejected(string filter, string hint)
    {
        var runId = await SyntheticRunAsync();

        var response = await Client.GetAsync($"/api/runs/{runId}/violations?{filter}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(hint, Assert.Single(await response.ValidationErrorsAsync()).Message);
    }

    // ---------------- preview ----------------

    [IntegrationFact]
    public async Task Preview_respects_its_row_limit_and_returns_canonical_values()
    {
        var response = await Client.PostJsonAsync("/api/controls/preview", $$"""{"maxRows":3,"definition":{{AllDeliveries}}}""");
        var preview = (await response.ReadAsync<PreviewDto>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DeliveryCount(), preview.TotalCount);
        Assert.Equal(3, preview.Items.Count);
        Assert.True(preview.Truncated);
        Assert.Equal("משלוחים", preview.RootEntityDisplayNamePlural);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}$", preview.Items[0].Values.GetProperty("DispatchedAt").GetString());
        Assert.Equal(JsonValueKind.Number, preview.Items[0].Values.GetProperty("PackageWeightKg").ValueKind);
    }
}
