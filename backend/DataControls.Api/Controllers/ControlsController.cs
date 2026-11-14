using DataControls.Api.Contracts;
using DataControls.Application.Abstractions;
using DataControls.Application.Catalog;
using DataControls.Application.Controls;
using DataControls.Application.Errors;
using DataControls.Application.Execution;
using DataControls.Application.Runs;
using DataControls.Domain.Controls;
using DataControls.Domain.Definition;
using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.Controllers;

/// <summary>Controls: definitions stored as data (a JSON condition tree), never as SQL.</summary>
[ApiController]
[Route("api/controls")]
public sealed class ControlsController(
    IControlService controls,
    IControlExecutionService execution,
    IRunService runs,
    IBusinessCatalogService catalog) : ControllerBase
{
    /// <summary>Lists controls with their last run.</summary>
    /// <param name="search">Free text in the name or description.</param>
    /// <param name="rootEntity">Only controls on this entity.</param>
    /// <param name="isActive">Only active / inactive controls.</param>
    /// <param name="source">Manual or AI.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ControlSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ControlSummaryDto>> List(string? search, string? rootEntity, bool? isActive,
        ControlSource? source, CancellationToken ct)
    {
        var items = await controls.ListAsync(new ControlListFilter(search, rootEntity, isActive, source), ct);
        var names = await EntityNamesAsync(ct);
        return items.Select(i => ControlSummaryDto.From(i, names)).ToList();
    }

    /// <summary>A control with its definition and rowVersion.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<ControlDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ControlDto> Get(int id, CancellationToken ct) =>
        ControlDto.From(await controls.GetAsync(id, ct), await EntityNamesAsync(ct));

    /// <summary>Creates a control. The definition is validated against the catalog before it is saved.</summary>
    [HttpPost]
    [ProducesResponseType<ControlDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ControlDto>> Create(SaveControlRequest request, CancellationToken ct)
    {
        var control = await controls.CreateAsync(ToInput(request), ct);
        return CreatedAtAction(nameof(Get), new { id = control.ControlId }, ControlDto.From(control, await EntityNamesAsync(ct)));
    }

    /// <summary>Updates a control. Requires the rowVersion from GET; a stale one returns 409.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType<ControlDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ControlDto> Update(int id, SaveControlRequest request, CancellationToken ct)
    {
        var control = await controls.UpdateAsync(id, ToInput(request), ParseRowVersion(request.RowVersion), ct);
        return ControlDto.From(control, await EntityNamesAsync(ct));
    }

    /// <summary>Deletes a control that was never run. A control with run history can only be deactivated (409).</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await controls.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Activates a control.</summary>
    [HttpPost("{id:int}/activate")]
    [ProducesResponseType<ControlDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ControlDto> Activate(int id, CancellationToken ct) =>
        ControlDto.From(await controls.SetActiveAsync(id, true, ct), await EntityNamesAsync(ct));

    /// <summary>Deactivates a control (it stays in the history, but cannot be run).</summary>
    [HttpPost("{id:int}/deactivate")]
    [ProducesResponseType<ControlDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ControlDto> Deactivate(int id, CancellationToken ct) =>
        ControlDto.From(await controls.SetActiveAsync(id, false, ct), await EntityNamesAsync(ct));

    /// <summary>Checks a definition against the catalog without saving it.
    /// Always 200: an invalid definition returns isValid=false with Hebrew errors and their paths.</summary>
    [HttpPost("validate")]
    [ProducesResponseType<DefinitionCheckDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<DefinitionCheckDto> Validate(DefinitionRequest request, CancellationToken ct)
    {
        var check = await controls.CheckDefinitionAsync(request.Definition!.Value.GetRawText(), ct);
        return new DefinitionCheckDto(check.IsValid, check.RootEntityKey, check.RootEntityDisplayName,
            check.Errors.Select(ValidationErrorDto.From).ToList());
    }

    /// <summary>Runs an unsaved definition (e.g. an AI suggestion before approval). Nothing is recorded.
    /// Returns at most maxRows records (1–100, default 50).</summary>
    [HttpPost("preview")]
    [ProducesResponseType<PreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<PreviewDto> Preview(PreviewRequest request, CancellationToken ct)
    {
        var definition = ControlDefinitionSerializer.Parse(request.Definition!.Value.GetRawText());
        return PreviewDto.From(await execution.PreviewAsync(definition, request.MaxRows, ct));
    }

    /// <summary>Runs a saved control. The run and its failing records are recorded and can be paged later.</summary>
    /// <param name="id">Control id.</param>
    /// <param name="maxRows">Optional, lower than the configured row limit.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:int}/runs")]
    [ProducesResponseType<RunDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RunDto>> Run(int id, int? maxRows, CancellationToken ct)
    {
        if (maxRows is < 1)
            throw new InvalidRequestException("maxRows", "מספר הרשומות המרבי חייב להיות 1 או יותר.");

        var result = await execution.RunControlAsync(id, maxRows, ct);
        var run = await runs.GetAsync(result.RunId!.Value, ct);
        return CreatedAtAction(nameof(RunsController.Get), "Runs", new { runId = run.RunId }, RunDto.From(run));
    }

    private static ControlInput ToInput(SaveControlRequest request) => new(
        request.Name, request.Description, request.Severity, request.Source, request.IsActive,
        request.Definition!.Value.GetRawText());

    private static byte[] ParseRowVersion(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) return [];
        try
        {
            return Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new InvalidRequestException("rowVersion", "גרסת הבקרה (rowVersion) אינה תקינה. יש לטעון את הבקרה מחדש.");
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> EntityNamesAsync(CancellationToken ct) =>
        (await catalog.GetEntitiesAsync(ct)).ToDictionary(e => e.Key, e => e.DisplayName, StringComparer.OrdinalIgnoreCase);
}
