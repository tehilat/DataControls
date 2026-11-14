using System.Diagnostics;
using System.Globalization;
using System.Text;
using DataControls.Application.Abstractions;
using DataControls.Application.Binding;
using DataControls.Application.Errors;
using DataControls.Application.Querying;
using DataControls.Application.Results;
using DataControls.Application.Runs;
using DataControls.Application.Formatting;
using DataControls.Domain.Controls;
using DataControls.Domain.Definition;
using DataControls.Domain.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataControls.Application.Execution;

public interface IControlExecutionService
{
    /// <summary>Runs a saved control and records the run in ctl.ControlRuns.</summary>
    Task<ControlRunResult> RunControlAsync(int controlId, int? maxRows = null, CancellationToken ct = default);

    /// <summary>Runs an unsaved definition (e.g. before approving an AI suggestion). Not recorded.</summary>
    Task<ControlRunResult> PreviewAsync(ControlDefinition definition, int? maxRows = null, CancellationToken ct = default);

    /// <summary>Validates a definition against the catalog; throws <see cref="ControlValidationException"/>.</summary>
    Task<BoundDefinition> ValidateAsync(ControlDefinition definition, CancellationToken ct = default);
}

/// <summary>Definition (JSON) → Binder → QueryBuilder → Runner → reasons.</summary>
public sealed class ControlExecutionService(
    IControlRepository controls,
    IControlRunRepository runs,
    IMetadataProvider metadata,
    DefinitionBinder binder,
    IQueryBuilder queryBuilder,
    IControlRunner runner,
    ViolationReasonBuilder reasons,
    IOptions<ControlExecutionOptions> options,
    ILogger<ControlExecutionService> logger) : IControlExecutionService
{
    private const int MaxErrorLength = 2000;

    public async Task<ControlRunResult> RunControlAsync(int controlId, int? maxRows = null, CancellationToken ct = default)
    {
        var control = await controls.GetByIdAsync(controlId, ct) ?? throw new ControlNotFoundException(controlId);
        if (!control.IsActive)
            throw new ConflictException($"הבקרה \"{control.Name}\" מושבתת. יש להפעיל אותה לפני הרצה.");

        ControlDefinition definition;
        try
        {
            definition = ControlDefinitionSerializer.Parse(control.DefinitionJson);
        }
        catch (DefinitionFormatException ex)
        {
            throw new ControlValidationException([new ValidationError("", ex.Message)]);
        }

        if (!string.Equals(definition.RootEntity, control.RootEntityKey, StringComparison.OrdinalIgnoreCase))
            throw new ControlValidationException([new ValidationError("rootEntity",
                "ישות הבסיס בהגדרת הבקרה אינה תואמת את ישות הבסיס של הבקרה השמורה.")]);

        var catalog = await metadata.GetCatalogAsync(ct);
        var bound = binder.Bind(definition, catalog);
        var query = queryBuilder.Build(bound, EffectiveMaxRows(maxRows));

        var runId = await runs.StartAsync(controlId, query.Sql, ct);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var raw = await runner.RunAsync(query, ct);
            stopwatch.Stop();
            var result = MapResult(bound, query, raw, catalog, runId, controlId, stopwatch.ElapsedMilliseconds);

            await runs.SaveResultsAsync(runId, result.Columns, ToStored(result, query, catalog), CancellationToken.None);
            await runs.CompleteAsync(runId, ControlRunStatus.Succeeded, result.TotalCount, stopwatch.ElapsedMilliseconds, null, CancellationToken.None);
            logger.LogInformation("Control {ControlId} run {RunId}: {Count} violations in {Ms} ms",
                controlId, runId, result.TotalCount, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var message = ex is ControlExecutionException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            if (message.Length > MaxErrorLength) message = message[..MaxErrorLength];

            await runs.CompleteAsync(runId, ControlRunStatus.Failed, null, stopwatch.ElapsedMilliseconds, message, CancellationToken.None);
            logger.LogWarning(ex, "Control {ControlId} run {RunId} failed", controlId, runId);
            if (ex is ControlExecutionException execution)
                throw new ControlExecutionException(execution.Message, execution.InnerException) { RunId = runId };
            throw;
        }
    }

    public async Task<ControlRunResult> PreviewAsync(ControlDefinition definition, int? maxRows = null, CancellationToken ct = default)
    {
        var catalog = await metadata.GetCatalogAsync(ct);
        var bound = binder.Bind(definition, catalog);
        var query = queryBuilder.Build(bound, EffectivePreviewRows(maxRows));

        var stopwatch = Stopwatch.StartNew();
        var raw = await runner.RunAsync(query, ct);
        stopwatch.Stop();
        return MapResult(bound, query, raw, catalog, runId: null, controlId: null, stopwatch.ElapsedMilliseconds);
    }

    public async Task<BoundDefinition> ValidateAsync(ControlDefinition definition, CancellationToken ct = default)
    {
        var catalog = await metadata.GetCatalogAsync(ct);
        return binder.Bind(definition, catalog);
    }

    private int EffectiveMaxRows(int? requested) =>
        Math.Clamp(requested ?? options.Value.MaxRows, 1, options.Value.MaxRows);

    /// <summary>Previews are always small, whoever calls them (UI or the AI flow).</summary>
    private int EffectivePreviewRows(int? requested)
    {
        var limit = Math.Min(options.Value.PreviewMaxRows, options.Value.MaxRows);
        return Math.Clamp(requested ?? options.Value.PreviewDefaultRows, 1, limit);
    }

    private const int MaxStoredReasonLength = 4000;
    private const int MaxStoredDisplayLength = 400;
    private const int MaxStoredIdLength = 100;

    /// <summary>Rows for ctl.ControlRunViolations: canonical values + a search text in the user's terms.</summary>
    private static List<StoredViolation> ToStored(ControlRunResult result, BuiltQuery query,
        MetadataCatalog catalog)
    {
        var stored = new List<StoredViolation>(result.Violations.Count);
        for (var i = 0; i < result.Violations.Count; i++)
        {
            var v = result.Violations[i];
            var search = new StringBuilder();
            search.Append(v.DisplayValue).Append(" | ").Append(v.Reason);
            foreach (var column in query.Columns)
            {
                var value = v.Values.GetValueOrDefault(column.Field.FieldKey);
                if (value is not null)
                    search.Append(" | ").Append(ValueFormatter.Format(value, column.Field, catalog));
            }

            stored.Add(new StoredViolation(
                i + 1,
                Clip(Convert.ToString(v.Id, CultureInfo.InvariantCulture), MaxStoredIdLength),
                Clip(v.DisplayValue, MaxStoredDisplayLength),
                CanonicalValueJson.Serialize(v.Values, result.Columns),
                Clip(v.Reason, MaxStoredReasonLength) ?? "",
                search.ToString()));
        }
        return stored;
    }

    private static string? Clip(string? text, int max) =>
        text is null || text.Length <= max ? text : text[..max];

    private ControlRunResult MapResult(BoundDefinition bound, BuiltQuery query, RawQueryResult raw,
        MetadataCatalog catalog, int? runId, int? controlId, long durationMs)
    {
        var columns = query.Columns
            .Select(c => new ResultColumn(c.Field.FieldKey, c.Field.DisplayName, c.Field.DataType, c.Field.Unit))
            .ToList();

        var violations = raw.Rows.Select(row =>
        {
            var values = query.Columns.ToDictionary(
                c => c.Field.FieldKey,
                c => row.TryGetValue(c.Alias, out var v) && v is not DBNull ? v : null);

            return new ViolationRecord(
                row.GetValueOrDefault(ResultAliases.Id),
                Convert.ToString(row.GetValueOrDefault(ResultAliases.Display), CultureInfo.InvariantCulture),
                values,
                reasons.Build(bound, catalog, row));
        }).ToList();

        var total = raw.Rows.Count == 0
            ? 0
            : Convert.ToInt32(raw.Rows[0][ResultAliases.Total], CultureInfo.InvariantCulture);

        return new ControlRunResult(runId, controlId, bound.RootEntity.EntityKey, bound.RootEntity.DisplayNamePlural,
            total, total > violations.Count, columns, violations, durationMs);
    }
}
