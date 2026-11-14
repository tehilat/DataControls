using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DataControls.Application.Ai;
using DataControls.Application.Errors;
using Microsoft.Extensions.Logging;

namespace DataControls.Infrastructure.Ai;

/// <summary>Configuration section "AiService".</summary>
public sealed class AiServiceOptions
{
    public const string SectionName = "AiService";

    /// <summary>Base address of the Python AI service.</summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "http://localhost:8001";

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// <see cref="IAiControlGenerator"/> over HTTP to the Python AI service (ai-service/main.py).
/// Which model answers (mock or real) is decided entirely by that service. Every failure — unreachable,
/// timeout, error status, malformed answer — becomes <see cref="AiServiceUnavailableException"/>;
/// the answer itself is not trusted here, it is validated by <see cref="AiSuggestionService"/>.
/// </summary>
public sealed class HttpAiControlGenerator(HttpClient http, ILogger<HttpAiControlGenerator> logger) : IAiControlGenerator
{
    public const string GeneratePath = "generate-control";
    public const string PromptPreviewPath = "prompt-preview";

    /// <summary>Upper bound on an answer we are willing to read.</summary>
    public const int MaxResponseBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AiGeneratorResult> GenerateAsync(string text, AiCatalog catalog, CancellationToken ct = default)
    {
        var answer = await PostAsync<GenerateResponseWire>(GeneratePath, text, catalog, ct);

        var definition = answer.Definition is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } d
            ? d.GetRawText()
            : null;

        return new AiGeneratorResult(
            definition,
            answer.Explanation ?? "",
            answer.Confidence is { } c && double.IsFinite(c) ? c : 0,
            answer.Warnings?.Where(w => w is not null).Select(w => w!).ToList() ?? []);
    }

    public async Task<AiPromptPreview> PreviewPromptAsync(string text, AiCatalog catalog, CancellationToken ct = default)
    {
        var answer = await PostAsync<PromptPreviewWire>(PromptPreviewPath, text, catalog, ct);
        if (answer.System is null || answer.User is null)
            throw Malformed(PromptPreviewPath, null);

        return new AiPromptPreview(answer.Provider ?? "", answer.System, answer.User,
            answer.ResponseSchema ?? JsonDocument.Parse("{}").RootElement.Clone());
    }

    private async Task<T> PostAsync<T>(string path, string text, AiCatalog catalog, CancellationToken ct) where T : class
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(path, new GenerateRequestWire(text, catalog), Json, ct);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "AI service timed out ({Path})", path);
            throw new AiServiceUnavailableException("שירות ה-AI לא השיב בזמן. ניתן לנסות שוב או להגדיר את הבקרה ידנית.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "AI service unreachable ({Path})", path);
            throw new AiServiceUnavailableException(AiServiceUnavailableException.DefaultMessage, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(response, ct);
                logger.LogWarning("AI service returned {Status} for {Path}: {Body}", (int)response.StatusCode, path, body);
                // 503 = the service runs but is not configured (unknown AI_PROVIDER, missing API key).
                // Its Hebrew "detail" says what to fix, so it is shown to the user instead of the generic message.
                var reason = response.StatusCode == HttpStatusCode.ServiceUnavailable ? ConfigurationReason(body) : null;
                throw new AiServiceUnavailableException(reason ?? AiServiceUnavailableException.DefaultMessage);
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw Malformed(path, null);
            }
            catch (JsonException ex)
            {
                throw Malformed(path, ex);
            }
            catch (NotSupportedException ex) // wrong content type
            {
                throw Malformed(path, ex);
            }
        }
    }

    private AiServiceUnavailableException Malformed(string path, Exception? inner)
    {
        logger.LogWarning(inner, "AI service returned a malformed answer ({Path})", path);
        return new AiServiceUnavailableException("התקבלה תשובה לא תקינה משירות ה-AI. ניתן לנסות שוב או להגדיר את הבקרה ידנית.", inner);
    }

    /// <summary>Upper bound on a configuration message passed on to the user.</summary>
    public const int MaxReasonLength = 300;

    /// <summary>The "detail" of the service's problem+json, if it is a short, plain message; otherwise null.</summary>
    internal static string? ConfigurationReason(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("detail", out var detail)
                || detail.ValueKind != JsonValueKind.String)
                return null;

            var text = detail.GetString()?.Trim();
            if (string.IsNullOrEmpty(text) || text.Length > MaxReasonLength || text.Any(char.IsControl)) return null;
            return text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return body.Length > 500 ? body[..500] + "…" : body;
        }
        catch (Exception)
        {
            return "";
        }
    }

    // wire shapes of ai-service/schemas.py (camelCase JSON)
    private sealed record GenerateRequestWire(string Text, AiCatalog Catalog);

    private sealed record GenerateResponseWire(JsonElement? Definition, string? Explanation, double? Confidence, List<string?>? Warnings);

    private sealed record PromptPreviewWire(string? Provider, string? System, string? User, JsonElement? ResponseSchema);
}
