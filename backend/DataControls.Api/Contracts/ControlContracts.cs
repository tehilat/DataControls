using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DataControls.Application.Abstractions;
using DataControls.Application.Errors;
using DataControls.Application.Runs;
using DataControls.Domain.Controls;

namespace DataControls.Api.Contracts;

public sealed record LastRunDto(int RunId, DateTime StartedAt, ControlRunStatus Status, int? ViolationCount)
{
    public static LastRunDto? From(RunSummary? r) => r is null ? null : new(r.RunId, r.StartedAt, r.Status, r.ViolationCount);
}

/// <summary>A control in the list (without its definition).</summary>
public sealed record ControlSummaryDto(
    int Id,
    string Name,
    string? Description,
    string RootEntity,
    string? RootEntityDisplayName,
    ControlSeverity Severity,
    ControlSource Source,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    LastRunDto? LastRun)
{
    public static ControlSummaryDto From(ControlListItem item, IReadOnlyDictionary<string, string> entityNames)
    {
        var c = item.Control;
        return new(c.ControlId, c.Name, c.Description, c.RootEntityKey, entityNames.GetValueOrDefault(c.RootEntityKey),
            c.Severity, c.Source, c.IsActive, c.CreatedAt, c.UpdatedAt, LastRunDto.From(item.LastRun));
    }
}

/// <summary>A control with its definition (JSON condition tree) and its concurrency version.</summary>
/// <param name="RowVersion">Send back unchanged in PUT; a stale value returns 409.</param>
public sealed record ControlDto(
    int Id,
    string Name,
    string? Description,
    string RootEntity,
    string? RootEntityDisplayName,
    ControlSeverity Severity,
    ControlSource Source,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    JsonElement Definition,
    string RowVersion)
{
    public static ControlDto From(Control c, IReadOnlyDictionary<string, string> entityNames)
    {
        using var document = JsonDocument.Parse(c.DefinitionJson);
        return new(c.ControlId, c.Name, c.Description, c.RootEntityKey, entityNames.GetValueOrDefault(c.RootEntityKey),
            c.Severity, c.Source, c.IsActive, c.CreatedAt, c.UpdatedAt, document.RootElement.Clone(),
            Convert.ToBase64String(c.RowVersion));
    }
}

/// <summary>Create (POST) or update (PUT) a control.</summary>
public sealed class SaveControlRequest
{
    /// <summary>Required; validated together with the definition so all errors are reported at once.</summary>
    public string? Name { get; init; }

    public string? Description { get; init; }

    public ControlSeverity Severity { get; init; } = ControlSeverity.Medium;

    public ControlSource Source { get; init; } = ControlSource.Manual;

    public bool IsActive { get; init; } = true;

    /// <summary>The control definition: { version, rootEntity, condition }.</summary>
    [Required(ErrorMessage = "חסרה הגדרת הבקרה.")]
    public JsonElement? Definition { get; init; }

    /// <summary>Required for update: the rowVersion received from GET.</summary>
    public string? RowVersion { get; init; }
}

public sealed class DefinitionRequest
{
    [Required(ErrorMessage = "חסרה הגדרת הבקרה.")]
    public JsonElement? Definition { get; init; }
}

public sealed class PreviewRequest
{
    [Required(ErrorMessage = "חסרה הגדרת הבקרה.")]
    public JsonElement? Definition { get; init; }

    /// <summary>1–100, default 50.</summary>
    [Range(1, 100, ErrorMessage = "מספר הרשומות בתצוגה מקדימה חייב להיות בין 1 ל-100.")]
    public int? MaxRows { get; init; }
}

public sealed record ValidationErrorDto(string Path, string Message)
{
    public static ValidationErrorDto From(ValidationError e) => new(e.Path, e.Message);
}

/// <summary>Result of checking a definition. Paths are relative to the definition, e.g. "condition.children[0].operator".</summary>
public sealed record DefinitionCheckDto(
    bool IsValid,
    string? RootEntity,
    string? RootEntityDisplayName,
    IReadOnlyList<ValidationErrorDto> Errors);
