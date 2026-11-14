using System.ComponentModel.DataAnnotations;

namespace DataControls.Application.Execution;

/// <summary>Configuration section "ControlExecution".</summary>
public sealed class ControlExecutionOptions
{
    public const string SectionName = "ControlExecution";

    /// <summary>Upper bound on returned rows (TOP). The total count is still reported.</summary>
    [Range(1, 10_000)]
    public int MaxRows { get; set; } = 1000;

    /// <summary>Upper bound for a preview (an unsaved definition, e.g. an AI suggestion before approval).</summary>
    [Range(1, 1000)]
    public int PreviewMaxRows { get; set; } = 100;

    /// <summary>Rows returned by a preview when the caller does not ask for a number.</summary>
    [Range(1, 1000)]
    public int PreviewDefaultRows { get; set; } = 50;

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Database user the generated query runs as (EXECUTE AS USER). SELECT on ops only.</summary>
    [Required, RegularExpression("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    public string RunnerUser { get; set; } = "controls_runner";
}
