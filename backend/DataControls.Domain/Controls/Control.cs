namespace DataControls.Domain.Controls;

public enum ControlSeverity { Low, Medium, High }

public enum ControlSource { Manual, AI }

public enum ControlRunStatus { Running, Succeeded, Failed }

/// <summary>A saved control (ctl.Controls). The definition is data (JSON), never SQL.</summary>
public sealed class Control
{
    public int ControlId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string RootEntityKey { get; init; } = string.Empty;
    public string DefinitionJson { get; init; } = string.Empty;
    public ControlSeverity Severity { get; init; }
    public ControlSource Source { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    /// <summary>Optimistic concurrency token (ctl.Controls.RowVer).</summary>
    public byte[] RowVersion { get; init; } = [];
}
