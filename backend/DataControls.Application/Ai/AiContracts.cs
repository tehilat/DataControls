using System.Text.Json;

namespace DataControls.Application.Ai;

// The contract with the AI service (ai-service/schemas.py). Business terms only: keys, field paths,
// Hebrew names, data types, operators and list values — never schema / table / column names.

/// <summary>The business catalog sent to the AI service with every request.</summary>
public sealed record AiCatalog(
    int Version,
    IReadOnlyList<AiCatalogEntity> Entities,
    IReadOnlyList<AiCatalogOperator> Operators,
    IReadOnlyList<AiTypeOperator> TypeOperators);

/// <param name="Fields">Every field path reachable from the entity (relations are already flattened).</param>
public sealed record AiCatalogEntity(
    string Key,
    string DisplayName,
    string? DisplayNamePlural,
    string? Description,
    IReadOnlyList<AiCatalogField> Fields);

/// <param name="Path">"FieldKey" or "RelationKey(.RelationKey)*.FieldKey".</param>
/// <param name="DataType">number | text | date | datetime | boolean | enum</param>
public sealed record AiCatalogField(
    string Path,
    string DisplayName,
    string? Description,
    string DataType,
    string? Unit,
    IReadOnlyList<AiFieldOption> Options);

public sealed record AiFieldOption(string Value, string DisplayName);

public sealed record AiCatalogOperator(string Key, string DisplayName, bool RequiresValue);

/// <param name="DisplayName">Type-specific wording, e.g. "לפני" for dates.</param>
public sealed record AiTypeOperator(string DataType, string Operator, string? DisplayName, bool AllowFieldCompare);

/// <summary>
/// The raw answer of the AI service. <see cref="DefinitionJson"/> is untrusted: it is parsed and bound by
/// <see cref="AiSuggestionService"/> exactly like a definition a user typed.
/// </summary>
public sealed record AiGeneratorResult(
    string? DefinitionJson,
    string Explanation,
    double Confidence,
    IReadOnlyList<string> Warnings);

/// <summary>The full prompt a real model would receive (for demonstration and debugging).</summary>
public sealed record AiPromptPreview(string Provider, string System, string User, JsonElement ResponseSchema);

/// <summary>
/// Translates free text into a control definition. Implemented over HTTP by the Python AI service;
/// the model behind it (mock or real) can be replaced without any change here.
/// </summary>
public interface IAiControlGenerator
{
    /// <exception cref="Errors.AiServiceUnavailableException">The AI service could not be reached or answered badly.</exception>
    Task<AiGeneratorResult> GenerateAsync(string text, AiCatalog catalog, CancellationToken ct = default);

    /// <exception cref="Errors.AiServiceUnavailableException">The AI service could not be reached or answered badly.</exception>
    Task<AiPromptPreview> PreviewPromptAsync(string text, AiCatalog catalog, CancellationToken ct = default);
}
