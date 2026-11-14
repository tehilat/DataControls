using DataControls.Application.Binding;
using DataControls.Application.Querying;
using DataControls.Application.Results;
using DataControls.Application.Runs;
using DataControls.Domain.Controls;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Abstractions;

/// <summary>Provides the (cached) business catalog.</summary>
public interface IMetadataProvider
{
    Task<MetadataCatalog> GetCatalogAsync(CancellationToken ct = default);

    /// <summary>Drop the cache, e.g. after an entity was added through meta (06_demo).</summary>
    void Invalidate();
}

public sealed record ControlListFilter(string? Search, string? RootEntityKey, bool? IsActive, ControlSource? Source);

public sealed record ControlListItem(Control Control, RunSummary? LastRun);

/// <summary>The writable part of a control.</summary>
public sealed record ControlDraft(
    string Name,
    string? Description,
    string RootEntityKey,
    string DefinitionJson,
    ControlSeverity Severity,
    ControlSource Source,
    bool IsActive);

public enum UpdateOutcome { Updated, NotFound, Conflict }

public interface IControlRepository
{
    Task<Control?> GetByIdAsync(int controlId, CancellationToken ct = default);
    Task<IReadOnlyList<Control>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ControlListItem>> ListAsync(ControlListFilter filter, CancellationToken ct = default);

    /// <exception cref="Errors.ConflictException">The name is already taken.</exception>
    Task<int> CreateAsync(ControlDraft draft, CancellationToken ct = default);

    /// <summary>Updates only when <paramref name="rowVersion"/> is still current.</summary>
    /// <exception cref="Errors.ConflictException">The name is already taken.</exception>
    Task<UpdateOutcome> UpdateAsync(int controlId, ControlDraft draft, byte[] rowVersion, CancellationToken ct = default);

    Task<bool> DeleteAsync(int controlId, CancellationToken ct = default);
    Task<bool> SetActiveAsync(int controlId, bool isActive, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, int? excludeControlId, CancellationToken ct = default);
    Task<bool> HasRunsAsync(int controlId, CancellationToken ct = default);
}

public interface IControlRunRepository
{
    Task<int> StartAsync(int controlId, string generatedSql, CancellationToken ct = default);

    Task CompleteAsync(int runId, ControlRunStatus status, int? violationCount, long durationMs,
        string? errorMessage, CancellationToken ct = default);

    /// <summary>Stores the column snapshot and the failing records of a run.</summary>
    Task SaveResultsAsync(int runId, IReadOnlyList<ResultColumn> columns, IReadOnlyList<StoredViolation> violations,
        CancellationToken ct = default);

    Task<ControlRunInfo?> GetAsync(int runId, CancellationToken ct = default);

    /// <summary>Run history, newest first. Items have no column snapshot.</summary>
    Task<PagedResult<ControlRunInfo>> ListAsync(RunListQuery query, CancellationToken ct = default);

    Task<PagedResult<StoredViolation>> GetViolationsAsync(int runId, ViolationQuery query, CancellationToken ct = default);
}

/// <summary>Translates a bound definition into a parameterized, read-only SELECT.</summary>
public interface IQueryBuilder
{
    BuiltQuery Build(BoundDefinition definition, int maxRows);
}

/// <summary>Executes a built query with least privilege, a timeout and a row limit.</summary>
public interface IControlRunner
{
    Task<RawQueryResult> RunAsync(BuiltQuery query, CancellationToken ct = default);
}
