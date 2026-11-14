using DataControls.Application.Abstractions;
using DataControls.Application.Errors;
using DataControls.Application.Execution;
using DataControls.Domain.Controls;
using DataControls.Domain.Definition;

namespace DataControls.Application.Controls;

/// <summary>What a user submits when creating or updating a control. The definition is raw JSON text.</summary>
public sealed record ControlInput(
    string? Name,
    string? Description,
    ControlSeverity Severity,
    ControlSource Source,
    bool IsActive,
    string DefinitionJson);

public sealed record DefinitionCheck(
    bool IsValid,
    string? RootEntityKey,
    string? RootEntityDisplayName,
    IReadOnlyList<ValidationError> Errors);

public interface IControlService
{
    Task<IReadOnlyList<ControlListItem>> ListAsync(ControlListFilter filter, CancellationToken ct = default);
    Task<Control> GetAsync(int controlId, CancellationToken ct = default);
    Task<Control> CreateAsync(ControlInput input, CancellationToken ct = default);
    Task<Control> UpdateAsync(int controlId, ControlInput input, byte[] rowVersion, CancellationToken ct = default);
    Task DeleteAsync(int controlId, CancellationToken ct = default);
    Task<Control> SetActiveAsync(int controlId, bool isActive, CancellationToken ct = default);

    /// <summary>Checks a definition against the catalog without saving anything. Never throws for an invalid definition.</summary>
    Task<DefinitionCheck> CheckDefinitionAsync(string definitionJson, CancellationToken ct = default);
}

/// <summary>
/// Control lifecycle. Every definition is validated by the same binder that the run uses before it is saved,
/// and is stored in canonical form, so a saved control is always runnable (unless the metadata changes later).
/// </summary>
public sealed class ControlService(IControlRepository repository, IControlExecutionService execution) : IControlService
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 1000;

    public Task<IReadOnlyList<ControlListItem>> ListAsync(ControlListFilter filter, CancellationToken ct = default) =>
        repository.ListAsync(filter, ct);

    public async Task<Control> GetAsync(int controlId, CancellationToken ct = default) =>
        await repository.GetByIdAsync(controlId, ct) ?? throw new ControlNotFoundException(controlId);

    public async Task<Control> CreateAsync(ControlInput input, CancellationToken ct = default)
    {
        var draft = await PrepareAsync(input, ct);
        if (await repository.NameExistsAsync(draft.Name, null, ct))
            throw DuplicateName(draft.Name);

        var id = await repository.CreateAsync(draft, ct);
        return await GetAsync(id, ct);
    }

    public async Task<Control> UpdateAsync(int controlId, ControlInput input, byte[] rowVersion, CancellationToken ct = default)
    {
        _ = await GetAsync(controlId, ct);
        if (rowVersion is not { Length: > 0 })
            throw new InvalidRequestException("rowVersion", "חסרה גרסת הבקרה (rowVersion). יש לטעון את הבקרה מחדש לפני עדכון.");

        var draft = await PrepareAsync(input, ct);
        if (await repository.NameExistsAsync(draft.Name, controlId, ct))
            throw DuplicateName(draft.Name);

        return await repository.UpdateAsync(controlId, draft, rowVersion, ct) switch
        {
            UpdateOutcome.Updated => await GetAsync(controlId, ct),
            UpdateOutcome.NotFound => throw new ControlNotFoundException(controlId),
            _ => throw new ConflictException("הבקרה עודכנה בינתיים על ידי משתמש אחר. יש לטעון אותה מחדש ולנסות שוב.", "rowVersion")
        };
    }

    public async Task DeleteAsync(int controlId, CancellationToken ct = default)
    {
        var control = await GetAsync(controlId, ct);
        if (await repository.HasRunsAsync(controlId, ct))
            throw new ConflictException(
                $"לא ניתן למחוק את הבקרה \"{control.Name}\" כי יש לה היסטוריית הרצות. ניתן להשבית אותה במקום.");

        if (!await repository.DeleteAsync(controlId, ct))
            throw new ControlNotFoundException(controlId);
    }

    public async Task<Control> SetActiveAsync(int controlId, bool isActive, CancellationToken ct = default)
    {
        if (!await repository.SetActiveAsync(controlId, isActive, ct))
            throw new ControlNotFoundException(controlId);
        return await GetAsync(controlId, ct);
    }

    public async Task<DefinitionCheck> CheckDefinitionAsync(string definitionJson, CancellationToken ct = default)
    {
        try
        {
            var definition = ControlDefinitionSerializer.Parse(definitionJson);
            var bound = await execution.ValidateAsync(definition, ct);
            return new DefinitionCheck(true, bound.RootEntity.EntityKey, bound.RootEntity.DisplayName, []);
        }
        catch (DefinitionFormatException ex)
        {
            return new DefinitionCheck(false, null, null, [new ValidationError("", ex.Message)]);
        }
        catch (ControlValidationException ex)
        {
            return new DefinitionCheck(false, null, null, ex.Errors);
        }
    }

    /// <summary>Validates every part of the input and reports all problems together.</summary>
    private async Task<ControlDraft> PrepareAsync(ControlInput input, CancellationToken ct)
    {
        var errors = new List<ValidationError>();

        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0)
            errors.Add(new ValidationError("name", "יש להזין שם לבקרה."));
        else if (name.Length > MaxNameLength)
            errors.Add(new ValidationError("name", $"שם הבקרה ארוך מדי (עד {MaxNameLength} תווים)."));

        var description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        if (description is { Length: > MaxDescriptionLength })
            errors.Add(new ValidationError("description", $"התיאור ארוך מדי (עד {MaxDescriptionLength} תווים)."));

        if (!Enum.IsDefined(input.Severity))
            errors.Add(new ValidationError("severity", "רמת החומרה אינה תקינה."));
        if (!Enum.IsDefined(input.Source))
            errors.Add(new ValidationError("source", "מקור הבקרה אינו תקין."));

        string? canonicalJson = null;
        string? rootEntityKey = null;
        try
        {
            var definition = ControlDefinitionSerializer.Parse(input.DefinitionJson);
            var bound = await execution.ValidateAsync(definition, ct);
            canonicalJson = ControlDefinitionSerializer.Serialize(definition);
            rootEntityKey = bound.RootEntity.EntityKey;
        }
        catch (DefinitionFormatException ex)
        {
            errors.Add(new ValidationError("", ex.Message));
        }
        catch (ControlValidationException ex)
        {
            errors.AddRange(ex.Errors);
        }

        if (errors.Count > 0)
            throw new ControlValidationException(errors);

        return new ControlDraft(name, description, rootEntityKey!, canonicalJson!, input.Severity, input.Source, input.IsActive);
    }

    private static ConflictException DuplicateName(string name) =>
        new($"כבר קיימת בקרה בשם \"{name}\".", "name");
}
