using System.Net;
using DataControls.Api.Contracts;
using DataControls.Application.Catalog;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static DataControls.Tests.Support.Defs;

namespace DataControls.Tests.Api;

/// <summary>Validation, error format, CORS and OpenAPI — on the in-memory catalog (no database).</summary>
public class ApiBehaviorTests(CatalogOnlyApiFactory factory) : IClassFixture<CatalogOnlyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string Body(string definitionJson) => $$"""{"definition":{{definitionJson}}}""";

    // ---------------- validate ----------------

    [Fact]
    public async Task Validate_accepts_a_valid_definition_and_names_its_root_entity()
    {
        var response = await _client.PostJsonAsync("/api/controls/validate",
            Body(Json("delivery", Rule("PackageWeightKg", "gt", Field("Drone.MaxPayloadKg")))));

        var check = await response.ReadAsync<DefinitionCheckDto>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(check!.IsValid);
        Assert.Equal("Delivery", check.RootEntity); // canonical key from the catalog
        Assert.Equal("משלוח", check.RootEntityDisplayName);
        Assert.Empty(check.Errors);
    }

    [Fact]
    public async Task Validate_returns_every_error_in_hebrew_with_its_path_in_the_tree()
    {
        var response = await _client.PostJsonAsync("/api/controls/validate", Body(Json("Delivery",
            Group("AND",
                Rule("PackageWeightKg", "contains", Value("\"5\"")),
                Group("OR", Rule("Drone.NoSuchField", "eq", Value("1")))))));

        var check = (await response.ReadAsync<DefinitionCheckDto>())!;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(check.IsValid);
        Assert.Contains(check.Errors, e => e.Path == "condition.children[0].operator" && e.Message.Contains("אינו מתאים"));
        Assert.Contains(check.Errors, e => e.Path == "condition.children[1].children[0].field" && e.Message.Contains("אינו קיים"));
    }

    [Fact]
    public async Task Validate_reports_a_malformed_definition_without_throwing()
    {
        var response = await _client.PostJsonAsync("/api/controls/validate",
            """{"definition":{"version":1,"rootEntity":"Delivery","condition":{"type":"rule","field":"x","operator":"eq","sql":"DROP"}}}""");

        var check = (await response.ReadAsync<DefinitionCheckDto>())!;
        Assert.False(check.IsValid);
        Assert.Contains("אינו תקין", Assert.Single(check.Errors).Message);
    }

    // ---------------- error format ----------------

    [Fact]
    public async Task Missing_definition_is_a_hebrew_validation_problem()
    {
        var response = await _client.PostJsonAsync("/api/controls/validate", "{}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.JsonAsync();
        Assert.Equal("הבקשה אינה תקינה", body.GetProperty("title").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
        Assert.Equal(("definition", "חסרה הגדרת הבקרה."), Assert.Single(await response.ValidationErrorsAsync()));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    public async Task Malformed_body_is_a_hebrew_400(string body)
    {
        var response = await _client.PostJsonAsync("/api/controls/validate", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.All(await response.ValidationErrorsAsync(), e => Assert.Matches("[א-ת]", e.Message));
    }

    [Fact]
    public async Task Invalid_enum_value_names_the_field()
    {
        var response = await _client.PostJsonAsync("/api/controls",
            """{"name":"x","severity":"Huge","definition":{"version":1}}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (path, message) = Assert.Single(await response.ValidationErrorsAsync());
        Assert.Equal("severity", path);
        Assert.Contains("אינו תקין", message);
    }

    [Fact]
    public async Task Wrong_content_type_is_a_415_problem_in_hebrew()
    {
        var response = await _client.PostAsync("/api/controls/validate", new StringContent("x", null, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Contains("JSON", (await response.JsonAsync()).GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Preview_row_limit_is_1_to_100(int maxRows)
    {
        var response = await _client.PostJsonAsync("/api/controls/preview",
            $$"""{"maxRows":{{maxRows}},"definition":{{Json("Delivery", Rule("DeliveryId", "gt", Value("0")))}}}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("maxRows", Assert.Single(await response.ValidationErrorsAsync()).Path);
    }

    [Fact]
    public async Task Invalid_definition_in_preview_never_reaches_the_database()
    {
        // this factory has no database: a 400 (not a connection error) proves validation happens before any SQL
        var response = await _client.PostJsonAsync("/api/controls/preview",
            Body(Json("Delivery", Rule("PackageWeightKg", "gt", Value("\"1; DROP TABLE ops.Deliveries\"")))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("הגדרת הבקרה אינה תקינה", (await response.JsonAsync()).GetProperty("title").GetString());
        Assert.Equal("condition.compareTo.value", Assert.Single(await response.ValidationErrorsAsync()).Path);
    }

    [Fact]
    public async Task Unexpected_errors_do_not_leak_details()
    {
        var client = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBusinessCatalogService>();
            services.AddScoped<IBusinessCatalogService, ThrowingCatalog>();
        })).CreateClient();

        var response = await client.GetAsync("/api/metadata/entities");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("שגיאת שרת", text);
        Assert.DoesNotContain(ThrowingCatalog.Secret, text);
        Assert.DoesNotContain("at DataControls", text);
    }

    [Fact]
    public async Task Unknown_route_is_a_problem()
    {
        var response = await _client.GetAsync("/api/nothing-here");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("הפריט המבוקש לא נמצא", (await response.JsonAsync()).GetProperty("title").GetString());
    }

    // ---------------- CORS / OpenAPI ----------------

    [Fact]
    public async Task Cors_preflight_allows_the_angular_dev_server()
    {
        var response = await Preflight("http://localhost:4200");

        Assert.Equal("http://localhost:4200", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Cors_preflight_rejects_other_origins()
    {
        var response = await Preflight("http://evil.example");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task OpenApi_document_and_swagger_ui_are_served()
    {
        var document = await _client.GetAsync("/openapi/v1.json");
        var text = await document.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Contains("/api/controls/{id}/runs", text);
        Assert.Contains("/api/runs/{runId}/violations", text);
        Assert.Contains("/api/metadata/entities/{key}/fields", text);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/swagger/index.html")).StatusCode);
    }

    private Task<HttpResponseMessage> Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/controls");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return _client.SendAsync(request);
    }

    private sealed class ThrowingCatalog : IBusinessCatalogService
    {
        public const string Secret = "Server=prod-db;Password=hunter2";

        public Task<IReadOnlyList<EntityView>> GetEntitiesAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException(Secret);

        public Task<IReadOnlyList<FieldPathView>> GetFieldsAsync(string entityKey, int relationDepth, CancellationToken ct = default) =>
            throw new InvalidOperationException(Secret);

        public Task<IReadOnlyList<DataTypeOperatorsView>> GetOperatorsAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException(Secret);
    }
}
