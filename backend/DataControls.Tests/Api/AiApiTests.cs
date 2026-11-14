using System.Net;
using DataControls.Application.Ai;
using DataControls.Application.Errors;
using DataControls.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static DataControls.Tests.Support.Defs;

namespace DataControls.Tests.Api;

/// <summary>The API with a fake AI generator on the in-memory catalog (no AI service, no database).</summary>
public sealed class FakeAiApiFactory : CatalogOnlyApiFactory
{
    public FakeAiControlGenerator Generator { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiControlGenerator>();
            services.AddSingleton<IAiControlGenerator>(Generator);
        });
    }
}

/// <summary>The real HTTP generator pointed at a port where nothing listens.</summary>
public sealed class UnreachableAiApiFactory : CatalogOnlyApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("AiService:BaseUrl", "http://127.0.0.1:1");
        builder.UseSetting("AiService:TimeoutSeconds", "10");
    }
}

public class AiApiTests(FakeAiApiFactory factory) : IClassFixture<FakeAiApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> Suggest(string json) => _client.PostJsonAsync("/api/ai/suggest-control", json);

    [Fact]
    public async Task Suggestion_is_returned_for_approval()
    {
        factory.Generator.Throw = null;
        factory.Generator.Result = new AiGeneratorResult(
            Json("Customer", Group("AND", Rule("Phone", "isEmpty"), Rule("Email", "isEmpty"))),
            "הבקרה תאתר לקוחות ללא פרטי קשר", 0.95, ["הערה"]);

        var response = await Suggest("""{"text":"לקוח בלי טלפון ובלי אימייל"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.JsonAsync();
        Assert.True(body.GetProperty("isValid").GetBoolean());
        Assert.Equal("Customer", body.GetProperty("definition").GetProperty("rootEntity").GetString());
        Assert.Equal("Customer", body.GetProperty("rootEntity").GetString());
        Assert.Equal("לקוח", body.GetProperty("rootEntityDisplayName").GetString());
        Assert.Equal("יאותרו לקוחות שבהם טלפון ריק וגם אימייל ריק.", body.GetProperty("summary").GetString());
        Assert.Equal("הבקרה תאתר לקוחות ללא פרטי קשר", body.GetProperty("explanation").GetString());
        Assert.Equal(0.95, body.GetProperty("confidence").GetDouble());
        Assert.Equal("הערה", body.GetProperty("warnings")[0].GetString());
        Assert.Equal(0, body.GetProperty("validationErrors").GetArrayLength());
    }

    [Fact]
    public async Task Invalid_suggestion_is_200_with_validation_errors()
    {
        factory.Generator.Throw = null;
        factory.Generator.Result = new AiGeneratorResult(Json("Delivery", Rule("Salary", "gt", Value("1"))), "x", 0.9, []);

        var body = await (await Suggest("""{"text":"x"}""")).JsonAsync();

        Assert.False(body.GetProperty("isValid").GetBoolean());
        Assert.Equal("Salary", body.GetProperty("definition").GetProperty("condition").GetProperty("field").GetString());
        Assert.Equal("condition.field", body.GetProperty("validationErrors")[0].GetProperty("path").GetString());
    }

    [Fact]
    public async Task Unrecognized_request_is_200_without_definition()
    {
        factory.Generator.Throw = null;
        factory.Generator.Result = new AiGeneratorResult(null, "לא הצלחתי", 0, ["הבקשה לא זוהתה"]);

        var body = await (await Suggest("""{"text":"פיצה"}""")).JsonAsync();

        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("definition").ValueKind);
        Assert.False(body.GetProperty("isValid").GetBoolean());
        Assert.Equal("הבקשה לא זוהתה", body.GetProperty("warnings")[0].GetString());
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"text":"   "}""")]
    public async Task Missing_text_is_400(string json)
    {
        factory.Generator.Throw = null;
        var response = await Suggest(json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(await response.ValidationErrorsAsync(), e => e.Path == "text");
    }

    [Fact]
    public async Task Too_long_text_is_400()
    {
        var response = await Suggest($$"""{"text":"{{new string('א', 2001)}}"}""");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ai_unavailable_is_503_problem_in_hebrew()
    {
        factory.Generator.Throw = new AiServiceUnavailableException(AiServiceUnavailableException.DefaultMessage);
        try
        {
            var response = await Suggest("""{"text":"x"}""");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.JsonAsync();
            Assert.Equal("שירות ה-AI אינו זמין", body.GetProperty("title").GetString());
            Assert.Equal(AiServiceUnavailableException.DefaultMessage, body.GetProperty("detail").GetString());
        }
        finally
        {
            factory.Generator.Throw = null;
        }
    }

    [Fact]
    public async Task Prompt_preview()
    {
        factory.Generator.Throw = null;
        var response = await _client.GetAsync("/api/ai/prompt-preview?text=" + Uri.EscapeDataString("משלוח באיחור"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.JsonAsync();
        Assert.Equal("fake", body.GetProperty("provider").GetString());
        Assert.Equal("user: משלוח באיחור", body.GetProperty("user").GetString());
        Assert.Equal("object", body.GetProperty("responseSchema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Prompt_preview_without_text_is_400()
    {
        var response = await _client.GetAsync("/api/ai/prompt-preview");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

public class AiServiceDownApiTests(UnreachableAiApiFactory factory) : IClassFixture<UnreachableAiApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Suggest_is_503_when_the_ai_service_is_not_running()
    {
        var response = await _client.PostJsonAsync("/api/ai/suggest-control", """{"text":"רחפן פעיל במוקד סגור"}""");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.JsonAsync();
        Assert.Equal(AiServiceUnavailableException.DefaultMessage, body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Prompt_preview_is_503_when_the_ai_service_is_not_running()
    {
        var response = await _client.GetAsync("/api/ai/prompt-preview?text=x");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task The_rest_of_the_api_keeps_working()
    {
        var response = await _client.GetAsync("/api/metadata/entities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
