using DataControls.Api.Contracts;
using DataControls.Application.Runs;
using DataControls.Domain.Controls;
using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.Controllers;

/// <summary>Run history and the stored failing records of each run.</summary>
[ApiController]
[Route("api/runs")]
public sealed class RunsController(IRunService runs) : ControllerBase
{
    private const string FilterPrefix = "filter[";

    /// <summary>Run history, newest first.</summary>
    /// <param name="controlId">Only runs of this control.</param>
    /// <param name="status">Running, Succeeded or Failed.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">1–200, default 50.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType<PageDto<RunListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<PageDto<RunListItemDto>> List(int? controlId, ControlRunStatus? status, CancellationToken ct,
        int page = 1, int pageSize = RunService.DefaultPageSize) =>
        PageDto<RunListItemDto>.From(await runs.ListAsync(controlId, status, page, pageSize, ct), RunListItemDto.From);

    /// <summary>One run with its result columns (and the Hebrew error if it failed).</summary>
    [HttpGet("{runId:int}")]
    [ProducesResponseType<RunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<RunDto> Get(int runId, CancellationToken ct) => RunDto.From(await runs.GetAsync(runId, ct));

    /// <summary>
    /// The failing records of a run, with paging, free-text search, typed filters and typed sorting.
    /// Filters are passed as <c>filter[columnKey]=value</c> (repeatable); values use the canonical format:
    /// numbers "12.5", dates "yyyy-MM-dd", date-times "yyyy-MM-ddTHH:mm:ss", booleans "true"/"false", list fields by raw value.
    /// </summary>
    /// <param name="runId">Run id.</param>
    /// <param name="search">Free text in the record name, the values (as displayed) and the reason.</param>
    /// <param name="sortBy">A column key of the run; numbers and dates are sorted by value, not as text.</param>
    /// <param name="sortDir">asc (default) or desc.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">1–200, default 50.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{runId:int}/violations")]
    [ProducesResponseType<PageDto<ViolationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<PageDto<ViolationDto>> GetViolations(int runId, string? search, string? sortBy, string? sortDir,
        CancellationToken ct, int page = 1, int pageSize = RunService.DefaultPageSize)
    {
        var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in Request.Query)
        {
            if (key.Length > FilterPrefix.Length + 1
                && key.StartsWith(FilterPrefix, StringComparison.OrdinalIgnoreCase)
                && key.EndsWith(']'))
            {
                filters[key[FilterPrefix.Length..^1]] = values.ToString();
            }
        }

        var result = await runs.GetViolationsAsync(runId,
            new ViolationRequest(search, filters, sortBy, sortDir, page, pageSize), ct);
        return PageDto<ViolationDto>.From(result, ViolationDto.From);
    }
}
