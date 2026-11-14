using System.ComponentModel.DataAnnotations;

namespace DataControls.Application.Execution;

/// <summary>Configuration section "ControlExecution".</summary>
public sealed class ControlExecutionOptions
{
    public const string SectionName = "ControlExecution";

    /// <summary>Upper bound on returned rows (TOP). The total count is still reported.</summary>
    [Range(1, 10_000)]
    public int MaxRows { get; set; } = 1000;

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Database user the generated query runs as (EXECUTE AS USER). SELECT on ops only.</summary>
    [Required, RegularExpression("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    public string RunnerUser { get; set; } = "controls_runner";
}
