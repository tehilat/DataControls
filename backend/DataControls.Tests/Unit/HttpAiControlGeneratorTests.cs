using System.Net;
using System.Text;
using System.Text.Json;
using DataControls.Application.Ai;
using DataControls.Application.Errors;
using DataControls.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataControls.Tests.Unit;

/// <summary>The HTTP client of the AI service against a fake HttpMessageHandler (no network).</summary>
public class HttpAiControlGeneratorTests
{
    private static readonly AiCatalog Catalog = new(1,
        [new AiCatalogEntity("Delivery", "משלוח", "משלוחים", null,
            [new AiCatalogField("Status", "סטטוס משלוח", null, "enum", null, [new AiFieldOption("Delivered", "נמסר")])])],
        [new AiCatalogOperator("eq", "שווה ל", true)],
        [new AiTypeOperator("enum", "eq", "הוא", false)]);

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return await respond(request);
        }
    }

    private static (HttpAiControlGenerator Generator, FakeHandler Handler) Create(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null)
    {
        var handler = new FakeHandler(respond);
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://ai.test/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(30)
        };
        return (new HttpAiControlGenerator(http, NullLogger<HttpAiControlGenerator>.Instance), handler);
    }

    private static Func<HttpRequestMessage, Task<HttpResponseMessage>> Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        _ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    [Fact]
    public async Task Posts_text_and_catalog_in_the_python_contract()
    {
        var (generator, handler) = Create(Json("""{"definition":null,"explanation":"x","confidence":0,"warnings":[]}"""));

        await generator.GenerateAsync("משלוח באיחור", Catalog);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("http://ai.test/generate-control", handler.Request.RequestUri!.ToString());

        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        Assert.Equal("משלוח באיחור", root.GetProperty("text").GetString());
        var catalog = root.GetProperty("catalog");
        Assert.Equal(1, catalog.GetProperty("version").GetInt32());
        var field = catalog.GetProperty("entities")[0].GetProperty("fields")[0];
        Assert.Equal("Status", field.GetProperty("path").GetString());
        Assert.Equal("enum", field.GetProperty("dataType").GetString());
        Assert.Equal("נמסר", field.GetProperty("options")[0].GetProperty("displayName").GetString());
        Assert.True(catalog.GetProperty("operators")[0].GetProperty("requiresValue").GetBoolean());
        Assert.False(catalog.GetProperty("typeOperators")[0].GetProperty("allowFieldCompare").GetBoolean());
    }

    [Fact]
    public async Task Parses_a_suggestion()
    {
        var (generator, _) = Create(Json("""
            {"definition":{"version":1,"rootEntity":"Customer","condition":{"type":"rule","field":"Phone","operator":"isEmpty"}},
             "explanation":"הסבר","confidence":0.95,"warnings":["א","ב"]}
            """));

        var result = await generator.GenerateAsync("x", Catalog);

        using var definition = JsonDocument.Parse(result.DefinitionJson!);
        Assert.Equal("Customer", definition.RootElement.GetProperty("rootEntity").GetString());
        Assert.Equal("הסבר", result.Explanation);
        Assert.Equal(0.95, result.Confidence);
        Assert.Equal(["א", "ב"], result.Warnings);
    }

    [Fact]
    public async Task Missing_definition_and_fields_get_safe_defaults()
    {
        var (generator, _) = Create(Json("""{"definition":null}"""));

        var result = await generator.GenerateAsync("x", Catalog);

        Assert.Null(result.DefinitionJson);
        Assert.Equal("", result.Explanation);
        Assert.Equal(0, result.Confidence);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Prompt_preview_is_parsed()
    {
        var (generator, handler) = Create(Json("""{"provider":"mock","system":"S","user":"U","responseSchema":{"type":"object"}}"""));

        var preview = await generator.PreviewPromptAsync("x", Catalog);

        Assert.Equal("http://ai.test/prompt-preview", handler.Request!.RequestUri!.ToString());
        Assert.Equal(("mock", "S", "U"), (preview.Provider, preview.System, preview.User));
        Assert.Equal("object", preview.ResponseSchema.GetProperty("type").GetString());
    }

    // ------------------------------------------------------------------ failures → AiServiceUnavailableException

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Error_status_is_unavailable(HttpStatusCode status)
    {
        var (generator, _) = Create(Json("""{"title":"boom"}""", status));
        await Assert.ThrowsAsync<AiServiceUnavailableException>(() => generator.GenerateAsync("x", Catalog));
    }

    [Fact]
    public async Task Connection_failure_is_unavailable()
    {
        var (generator, _) = Create(_ => throw new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<AiServiceUnavailableException>(() => generator.GenerateAsync("x", Catalog));
        Assert.Equal(AiServiceUnavailableException.DefaultMessage, ex.Message);
    }

    [Fact]
    public async Task Timeout_is_unavailable()
    {
        var (generator, _) = Create(async r =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, timeout: TimeSpan.FromMilliseconds(100));

        var ex = await Assert.ThrowsAsync<AiServiceUnavailableException>(() => generator.GenerateAsync("x", Catalog));
        Assert.Contains("בזמן", ex.Message);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_turned_into_unavailable()
    {
        using var cts = new CancellationTokenSource();
        var (generator, _) = Create(async r =>
        {
            await cts.CancelAsync();
            cts.Token.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generator.GenerateAsync("x", Catalog, cts.Token));
    }

    [Theory]
    [InlineData("not json", "application/json")]
    [InlineData("""{"definition":""", "application/json")]
    [InlineData("<html>proxy error</html>", "text/html")]
    [InlineData("null", "application/json")]
    public async Task Malformed_answer_is_unavailable(string body, string mediaType)
    {
        var (generator, _) = Create(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        }));

        await Assert.ThrowsAsync<AiServiceUnavailableException>(() => generator.GenerateAsync("x", Catalog));
    }

    [Fact]
    public async Task Prompt_preview_without_prompt_is_unavailable()
    {
        var (generator, _) = Create(Json("""{"provider":"mock"}"""));
        await Assert.ThrowsAsync<AiServiceUnavailableException>(() => generator.PreviewPromptAsync("x", Catalog));
    }
}
