using DataControls.Application.Abstractions;
using DataControls.Application.Binding;
using DataControls.Application.Catalog;
using DataControls.Application.Errors;
using DataControls.Application.Formatting;
using DataControls.Domain.Definition;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Ai;

/// <summary>
/// A control proposed by the AI, ready for the user's approval.
/// </summary>
/// <param name="DefinitionJson">Canonical when <paramref name="IsValid"/>; otherwise as received (null when the AI proposed nothing).</param>
/// <param name="Summary">Hebrew summary built from the catalog (what will actually run); only when valid.</param>
/// <param name="Explanation">The AI's own explanation (Hebrew).</param>
/// <param name="ValidationErrors">Why the proposed definition cannot be used as is (same messages as manual validation).</param>
public sealed record AiSuggestion(
    string? DefinitionJson,
    bool IsValid,
    string? RootEntityKey,
    string? RootEntityDisplayName,
    string? Summary,
    string Explanation,
    double Confidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ValidationError> ValidationErrors);

public interface IAiSuggestionService
{
    /// <summary>Asks the AI for a control definition and validates it. Never saves and never runs anything.</summary>
    Task<AiSuggestion> SuggestAsync(string? text, CancellationToken ct = default);

    /// <summary>The prompt the AI service builds for <paramref name="text"/> (demonstration).</summary>
    Task<AiPromptPreview> PreviewPromptAsync(string? text, CancellationToken ct = default);

    /// <summary>The business catalog as it is sent to the AI service.</summary>
    Task<AiCatalog> BuildCatalogAsync(CancellationToken ct = default);
}

/// <summary>
/// Free text → AI service → the same parse + bind pipeline as a manual definition.
/// Depends only on the catalog and the binder: it has no access to the control repository or the runner,
/// so a suggestion can never be saved or executed from here. Saving goes through ControlService after the
/// user approved it (Source = AI).
/// </summary>
public sealed class AiSuggestionService(
    IAiControlGenerator generator,
    IBusinessCatalogService businessCatalog,
    IMetadataProvider metadata,
    DefinitionBinder binder) : IAiSuggestionService
{
    public const int MaxTextLength = 2000;
    public const int CatalogVersion = 1;

    public async Task<AiSuggestion> SuggestAsync(string? text, CancellationToken ct = default)
    {
        var request = ValidateText(text);
        var result = await generator.GenerateAsync(request, await BuildCatalogAsync(ct), ct);

        var warnings = result.Warnings.Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
        var explanation = result.Explanation?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(result.DefinitionJson))
            return new AiSuggestion(null, false, null, null, null, explanation, 0, warnings, []);

        try
        {
            var definition = ControlDefinitionSerializer.Parse(result.DefinitionJson);
            var bound = binder.Bind(definition, await metadata.GetCatalogAsync(ct));
            return new AiSuggestion(
                ControlDefinitionSerializer.Serialize(definition),
                IsValid: true,
                bound.RootEntity.EntityKey,
                bound.RootEntity.DisplayName,
                DefinitionSummaryBuilder.Build(bound),
                explanation,
                Math.Clamp(result.Confidence, 0, 1),
                warnings,
                []);
        }
        catch (DefinitionFormatException ex)
        {
            return Invalid(result.DefinitionJson, explanation, warnings, [new ValidationError("", ex.Message)]);
        }
        catch (ControlValidationException ex)
        {
            return Invalid(result.DefinitionJson, explanation, warnings, ex.Errors);
        }
    }

    public async Task<AiPromptPreview> PreviewPromptAsync(string? text, CancellationToken ct = default) =>
        await generator.PreviewPromptAsync(ValidateText(text), await BuildCatalogAsync(ct), ct);

    public async Task<AiCatalog> BuildCatalogAsync(CancellationToken ct = default)
    {
        var entities = new List<AiCatalogEntity>();
        foreach (var entity in await businessCatalog.GetEntitiesAsync(ct))
        {
            var fields = (await businessCatalog.GetFieldsAsync(entity.Key, BusinessCatalogService.DefaultRelationDepth, ct))
                .Where(f => f.DataType != FieldDataType.Enum || f.Options.Count > 0) // a list without values is unusable
                .Select(f => new AiCatalogField(
                    f.Path,
                    f.FullDisplayName,
                    f.Description,
                    f.DataType.ToKey(),
                    f.Unit,
                    f.Options.Select(o => new AiFieldOption(o.Value, o.DisplayName)).ToList()))
                .ToList();

            if (fields.Count > 0)
                entities.Add(new AiCatalogEntity(entity.Key, entity.DisplayName, entity.DisplayNamePlural, entity.Description, fields));
        }

        // operators the engine can run, with their base names; per-type wording comes from the type rules
        var catalog = await metadata.GetCatalogAsync(ct);
        var operators = catalog.Operators
            .Where(o => ComparisonOperators.TryParse(o.OperatorKey, out _))
            .Select(o => new AiCatalogOperator(o.OperatorKey, o.DisplayName, o.RequiresValue))
            .ToList();

        var typeOperators = (await businessCatalog.GetOperatorsAsync(ct))
            .SelectMany(t => t.Operators.Select(o =>
                new AiTypeOperator(t.DataType.ToKey(), o.Key, o.DisplayName, o.AllowFieldCompare)))
            .ToList();

        return new AiCatalog(CatalogVersion, entities, operators, typeOperators);
    }

    private static AiSuggestion Invalid(string definitionJson, string explanation, List<string> warnings,
        IReadOnlyList<ValidationError> errors) =>
        new(definitionJson, false, null, null, null, explanation, 0, warnings, errors);

    private static string ValidateText(string? text)
    {
        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0)
            throw new InvalidRequestException("text", "יש לתאר את הבקרה במילים.");
        if (trimmed.Length > MaxTextLength)
            throw new InvalidRequestException("text", $"התיאור ארוך מדי (עד {MaxTextLength} תווים).");
        return trimmed;
    }
}
