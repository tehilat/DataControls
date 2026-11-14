using System.Text.Json;
using DataControls.Application.Ai;

namespace DataControls.Tests.Support;

/// <summary>Answers with a fixed result (or throws) and records what it was asked.</summary>
public sealed class FakeAiControlGenerator : IAiControlGenerator
{
    public AiGeneratorResult Result { get; set; } = new(null, "", 0, []);
    public Exception? Throw { get; set; }

    public string? LastText { get; private set; }
    public AiCatalog? LastCatalog { get; private set; }
    public int Calls { get; private set; }

    public static FakeAiControlGenerator Returning(string? definitionJson, string explanation = "הסבר",
        double confidence = 0.9, params string[] warnings) =>
        new() { Result = new AiGeneratorResult(definitionJson, explanation, confidence, warnings) };

    public Task<AiGeneratorResult> GenerateAsync(string text, AiCatalog catalog, CancellationToken ct = default)
    {
        Record(text, catalog);
        return Throw is null ? Task.FromResult(Result) : Task.FromException<AiGeneratorResult>(Throw);
    }

    public Task<AiPromptPreview> PreviewPromptAsync(string text, AiCatalog catalog, CancellationToken ct = default)
    {
        Record(text, catalog);
        if (Throw is not null) return Task.FromException<AiPromptPreview>(Throw);
        using var schema = JsonDocument.Parse("""{"type":"object"}""");
        return Task.FromResult(new AiPromptPreview("fake", "system: " + catalog.Entities.Count, "user: " + text,
            schema.RootElement.Clone()));
    }

    private void Record(string text, AiCatalog catalog)
    {
        LastText = text;
        LastCatalog = catalog;
        Calls++;
    }
}
