using DataControls.Application.Abstractions;
using DataControls.Application.Binding;
using DataControls.Application.Execution;
using DataControls.Application.Querying;
using DataControls.Application.Results;
using DataControls.Application.Runs;
using DataControls.Domain.Controls;
using DataControls.Domain.Definition;
using DataControls.Infrastructure.Querying;
using DataControls.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static DataControls.Tests.Support.Defs;

namespace DataControls.Tests.Unit;

/// <summary>A preview (unsaved definition, e.g. an AI suggestion) is always small, whoever calls it.</summary>
public class PreviewLimitTests
{
    [Theory]
    [InlineData(null, 50)]
    [InlineData(1, 1)]
    [InlineData(80, 80)]
    [InlineData(100, 100)]
    [InlineData(500, 100)]
    [InlineData(0, 1)]
    public async Task Preview_row_limit_defaults_to_50_and_is_capped_at_100(int? requested, int expected)
    {
        var builder = new CapturingQueryBuilder();
        var service = new ControlExecutionService(
            new NoControls(), new NoRuns(), new StaticMetadataProvider(Catalog), new DefinitionBinder(), builder,
            new EmptyRunner(), new ViolationReasonBuilder(),
            Options.Create(new ControlExecutionOptions { MaxRows = 1000 }),
            NullLogger<ControlExecutionService>.Instance);

        await service.PreviewAsync(ControlDefinitionSerializer.Parse(Json("Delivery", Rule("DeliveryId", "gt", Value("0")))), requested);

        Assert.Equal(expected, builder.MaxRows);
    }

    [Fact]
    public async Task Preview_never_exceeds_the_general_row_limit()
    {
        var builder = new CapturingQueryBuilder();
        var service = new ControlExecutionService(
            new NoControls(), new NoRuns(), new StaticMetadataProvider(Catalog), new DefinitionBinder(), builder,
            new EmptyRunner(), new ViolationReasonBuilder(),
            Options.Create(new ControlExecutionOptions { MaxRows = 20 }),
            NullLogger<ControlExecutionService>.Instance);

        await service.PreviewAsync(ControlDefinitionSerializer.Parse(Json("Delivery", Rule("DeliveryId", "gt", Value("0")))), 100);

        Assert.Equal(20, builder.MaxRows);
    }

    private sealed class CapturingQueryBuilder : IQueryBuilder
    {
        public int MaxRows { get; private set; }

        public BuiltQuery Build(BoundDefinition definition, int maxRows)
        {
            MaxRows = maxRows;
            return new SqlQueryBuilder().Build(definition, maxRows);
        }
    }

    private sealed class EmptyRunner : IControlRunner
    {
        public Task<RawQueryResult> RunAsync(BuiltQuery query, CancellationToken ct = default) =>
            Task.FromResult(new RawQueryResult([]));
    }

    private sealed class NoControls : IControlRepository
    {
        public Task<Control?> GetByIdAsync(int controlId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Control>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ControlListItem>> ListAsync(ControlListFilter filter, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CreateAsync(ControlDraft draft, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<UpdateOutcome> UpdateAsync(int controlId, ControlDraft draft, byte[] rowVersion, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(int controlId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SetActiveAsync(int controlId, bool isActive, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> NameExistsAsync(string name, int? excludeControlId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasRunsAsync(int controlId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Any attempt to record a run fails the test: a preview is never recorded.</summary>
    private sealed class NoRuns : IControlRunRepository
    {
        public Task<int> StartAsync(int controlId, string generatedSql, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CompleteAsync(int runId, ControlRunStatus status, int? violationCount, long durationMs, string? errorMessage, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveResultsAsync(int runId, IReadOnlyList<ResultColumn> columns, IReadOnlyList<StoredViolation> violations, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ControlRunInfo?> GetAsync(int runId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<ControlRunInfo>> ListAsync(RunListQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<StoredViolation>> GetViolationsAsync(int runId, ViolationQuery query, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
