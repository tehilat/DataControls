using DataControls.Application.Binding;
using DataControls.Application.Querying;
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

public interface IControlRepository
{
    Task<Control?> GetByIdAsync(int controlId, CancellationToken ct = default);
    Task<IReadOnlyList<Control>> GetAllAsync(CancellationToken ct = default);
}

public interface IControlRunRepository
{
    Task<int> StartAsync(int controlId, string generatedSql, CancellationToken ct = default);

    Task CompleteAsync(int runId, ControlRunStatus status, int? violationCount, long durationMs,
        string? errorMessage, CancellationToken ct = default);
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
