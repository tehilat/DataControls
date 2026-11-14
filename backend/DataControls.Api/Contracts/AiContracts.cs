using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DataControls.Application.Ai;

namespace DataControls.Api.Contracts;

public sealed class SuggestControlRequest
{
    /// <summary>The control described in free text (Hebrew), e.g. "רחפן פעיל במוקד סגור".</summary>
    [Required(ErrorMessage = "יש לתאר את הבקרה במילים.")]
    [MaxLength(AiSuggestionService.MaxTextLength, ErrorMessage = "התיאור ארוך מדי (עד 2000 תווים).")]
    public string? Text { get; init; }
}

/// <summary>
/// A control proposed by the AI for the user's approval. Nothing was saved or run.
/// To use it: show <see cref="Summary"/> + <see cref="Explanation"/> + <see cref="Warnings"/>, let the user edit it,
/// then save through POST /api/controls with source "AI" (or POST /api/controls/preview to try it first).
/// </summary>
/// <param name="Definition">The proposed definition (DefinitionJson); null when the AI could not translate the request.</param>
/// <param name="IsValid">Whether <paramref name="Definition"/> passed the same validation as a manual definition.</param>
/// <param name="Summary">Hebrew summary built from the catalog — what the control will actually check.</param>
/// <param name="Explanation">The AI's own explanation.</param>
/// <param name="Confidence">0–1, as reported by the AI (0 when the definition is missing or invalid).</param>
/// <param name="Warnings">Assumptions and notes from the AI (Hebrew).</param>
/// <param name="ValidationErrors">Why the definition cannot be used as is; paths are relative to the definition.</param>
public sealed record AiSuggestionDto(
    JsonElement? Definition,
    bool IsValid,
    string? RootEntity,
    string? RootEntityDisplayName,
    string? Summary,
    string Explanation,
    double Confidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ValidationErrorDto> ValidationErrors)
{
    public static AiSuggestionDto From(AiSuggestion s) => new(
        ParseJson(s.DefinitionJson),
        s.IsValid,
        s.RootEntityKey,
        s.RootEntityDisplayName,
        s.Summary,
        s.Explanation,
        s.Confidence,
        s.Warnings,
        s.ValidationErrors.Select(ValidationErrorDto.From).ToList());

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null; // not JSON at all; the validation errors say why
        }
    }
}

/// <summary>The prompt a real model would receive: system prompt (role, catalog, rules, format), user message and the answer schema.</summary>
public sealed record AiPromptPreviewDto(string Provider, string System, string User, JsonElement ResponseSchema)
{
    public static AiPromptPreviewDto From(AiPromptPreview p) => new(p.Provider, p.System, p.User, p.ResponseSchema);
}
