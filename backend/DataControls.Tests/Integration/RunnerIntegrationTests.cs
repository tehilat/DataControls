using System.Data;
using System.Globalization;
using DataControls.Application.Errors;
using DataControls.Application.Querying;
using DataControls.Domain.Definition;
using static DataControls.Tests.Support.Defs;

namespace DataControls.Tests.Integration;

[Trait("Category", "Integration")]
public class RunnerIntegrationTests
{
    private static async Task<int[]> PreviewIds(string definitionJson)
    {
        var result = await IntegrationDb.CreateService().PreviewAsync(ControlDefinitionSerializer.Parse(definitionJson));
        return result.Violations.Select(v => Convert.ToInt32(v.Id, CultureInfo.InvariantCulture)).Order().ToArray();
    }

    private static async Task<string> CurrentUserAsync() =>
        (await IntegrationDb.QueryAsync<string>("SELECT USER_NAME();")).Single();

    // ---------------- least privilege ----------------

    [IntegrationFact]
    public async Task Runner_session_is_reverted_after_a_run()
    {
        var before = await CurrentUserAsync();
        await IntegrationDb.CreateService().RunControlAsync(1);
        var after = await CurrentUserAsync(); // same pool → typically the very same physical connection

        Assert.NotEqual("controls_runner", after);
        Assert.Equal(before, after);
    }

    [IntegrationFact]
    public async Task Query_executes_as_controls_runner()
    {
        var result = await IntegrationDb.CreateRunner().RunAsync(
            new BuiltQuery("SELECT USER_NAME() AS [who];", [], []));
        Assert.Equal("controls_runner", result.Rows.Single()["who"]);
    }

    [IntegrationTheory]
    [InlineData("SELECT TOP 1 Name FROM ctl.Controls;")]
    [InlineData("SELECT TOP 1 DisplayName FROM meta.Fields;")]
    [InlineData("UPDATE ops.Deliveries SET PackageWeightKg = PackageWeightKg WHERE 1 = 0;")]
    [InlineData("DELETE FROM ops.Customers WHERE 1 = 0;")]
    public async Task Runner_cannot_touch_anything_but_reading_ops(string sql)
    {
        var ex = await Assert.ThrowsAsync<ControlExecutionException>(() =>
            IntegrationDb.CreateRunner().RunAsync(new BuiltQuery(sql, [], [])));
        Assert.Contains("permission", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("controls_runner", await CurrentUserAsync());
    }

    [IntegrationFact]
    public async Task Timeout_is_a_hebrew_execution_error_and_the_session_is_still_reverted()
    {
        var ex = await Assert.ThrowsAsync<ControlExecutionException>(() =>
            IntegrationDb.CreateRunner(timeoutSeconds: 1).RunAsync(
                new BuiltQuery("WAITFOR DELAY '00:00:05'; SELECT 1 AS [x];", [], [])));

        Assert.Contains("חרגה מזמן ההמתנה", ex.Message);
        Assert.NotEqual("controls_runner", await CurrentUserAsync());
    }

    // ---------------- row limit ----------------

    [IntegrationFact]
    public async Task Row_limit_truncates_but_reports_the_real_total()
    {
        var result = await IntegrationDb.CreateService(maxRows: 1).RunControlAsync(4);

        Assert.Single(result.Violations);
        Assert.Equal(2, result.TotalCount);
        Assert.True(result.Truncated);
    }

    // ---------------- run history ----------------

    [IntegrationFact]
    public async Task Run_is_recorded_with_generated_sql()
    {
        var result = await IntegrationDb.CreateService().RunControlAsync(12);

        var status = (await IntegrationDb.QueryAsync<string>(
            "SELECT Status FROM ctl.ControlRuns WHERE RunId = @id;", ("@id", result.RunId!.Value))).Single();
        var sql = (await IntegrationDb.QueryAsync<string>(
            "SELECT GeneratedSql FROM ctl.ControlRuns WHERE RunId = @id;", ("@id", result.RunId!.Value))).Single();
        var count = (await IntegrationDb.QueryAsync<int>(
            "SELECT ViolationCount FROM ctl.ControlRuns WHERE RunId = @id;", ("@id", result.RunId!.Value))).Single();

        Assert.Equal("Succeeded", status);
        Assert.StartsWith("SELECT TOP (@__maxRows)", sql);
        Assert.Equal(1, count);
    }

    // ---------------- Hebrew values (nvarchar parameters) ----------------

    [IntegrationFact]
    public async Task Hebrew_eq_matches_exactly()
    {
        var expected = (await IntegrationDb.QueryAsync<int>(
            "SELECT CustomerId FROM ops.Customers WHERE City = N'חיפה' ORDER BY CustomerId;")).ToArray();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, await PreviewIds(Json("Customer", Rule("City", "eq", Value("\"חיפה\"")))));
    }

    [IntegrationFact]
    public async Task Hebrew_contains_matches_substring_across_a_relation()
    {
        var expected = (await IntegrationDb.QueryAsync<int>("""
            SELECT d.DeliveryId FROM ops.Deliveries d JOIN ops.Customers c ON c.CustomerId = d.CustomerId
            WHERE c.City LIKE N'%אביב%' ORDER BY d.DeliveryId;
            """)).ToArray();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, await PreviewIds(Json("Delivery", Rule("Customer.City", "contains", Value("\"אביב\"")))));
    }

    [IntegrationFact]
    public async Task Hebrew_value_with_wildcards_and_quotes_is_literal() =>
        Assert.Empty(await PreviewIds(Json("Customer", Rule("City", "contains", Value("\"ת'%_א\"")))));

    // ---------------- emptiness: whitespace counts as empty ----------------

    [IntegrationFact]
    public async Task Whitespace_only_text_is_empty()
    {
        // temporary customer with a whitespace-only address; phone + email present so no sample control is affected
        var id = (await IntegrationDb.QueryAsync<int>("""
            INSERT INTO ops.Customers (FullName, Phone, Email, City, Address)
            OUTPUT INSERTED.CustomerId
            VALUES (N'בדיקת רווחים', N'050-0000000', N'spaces@example.com', N'בדיקה', N'   ');
            """)).Single();
        try
        {
            var empty = await PreviewIds(Json("Customer", Rule("Address", "isEmpty")));
            var notEmpty = await PreviewIds(Json("Customer", Rule("Address", "isNotEmpty")));

            Assert.Contains(id, empty);
            Assert.Contains(8, empty);          // Customer 8 has no address at all (NULL)
            Assert.DoesNotContain(id, notEmpty);
        }
        finally
        {
            await IntegrationDb.QueryAsync<int>("DELETE FROM ops.Customers WHERE CustomerId = @id; SELECT 0;", ("@id", id));
        }
    }

    // ---------------- range validation never reaches SQL ----------------

    [IntegrationFact]
    public async Task Out_of_range_values_fail_validation_before_sql()
    {
        var service = IntegrationDb.CreateService();
        await Assert.ThrowsAsync<ControlValidationException>(() => service.PreviewAsync(ControlDefinitionSerializer.Parse(
            Json("Delivery", Rule("PackageWeightKg", "gt", Value("123456789"))))));
        await Assert.ThrowsAsync<ControlValidationException>(() => service.PreviewAsync(ControlDefinitionSerializer.Parse(
            Json("Drone", Rule("NextMaintenanceDue", "lt", Today(3_000_000))))));
    }

    // ---------------- genericity: entity added through metadata only (06) ----------------

    [IntegrationFact]
    public async Task Entity_added_only_through_metadata_is_queryable()
    {
        var catalog = await IntegrationDb.CreateMetadataProvider().GetCatalogAsync();
        if (!catalog.TryGetEntity("WeatherAlert", out _)) return; // 06_demo not applied — nothing to prove

        Assert.Single(await PreviewIds(Json("WeatherAlert", Rule("WindSpeedKmh", "gt", Value("200")))));
        Assert.Single(await PreviewIds(Json("WeatherAlert", Rule("EndsAt", "lt", Field("StartsAt")))));
        await PreviewIds(Json("WeatherAlert", Rule("Hub.IsActive", "eq", Value("false"))));
    }
}
