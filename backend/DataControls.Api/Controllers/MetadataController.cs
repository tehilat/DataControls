using DataControls.Api.Contracts;
using DataControls.Application.Abstractions;
using DataControls.Application.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.Controllers;

/// <summary>The business catalog: entities, fields (also through relations), operators and list values.
/// Only business terms are returned — never physical table or column names.</summary>
[ApiController]
[Route("api/metadata")]
public sealed class MetadataController(IBusinessCatalogService catalog, IMetadataProvider metadata) : ControllerBase
{
    /// <summary>The entities a control can be defined on.</summary>
    [HttpGet("entities")]
    [ProducesResponseType<IReadOnlyList<EntityDto>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<EntityDto>> GetEntities(CancellationToken ct) =>
        (await catalog.GetEntitiesAsync(ct)).Select(EntityDto.From).ToList();

    /// <summary>The fields of an entity and of related entities, as paths usable in a condition.</summary>
    /// <param name="key">Entity key, e.g. "Delivery".</param>
    /// <param name="depth">How many relations to follow: 0–4, default 2.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("entities/{key}/fields")]
    [ProducesResponseType<IReadOnlyList<FieldDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IReadOnlyList<FieldDto>> GetFields(string key, CancellationToken ct,
        [FromQuery] int depth = BusinessCatalogService.DefaultRelationDepth) =>
        (await catalog.GetFieldsAsync(key, depth, ct)).Select(FieldDto.From).ToList();

    /// <summary>The operators allowed for each data type, with their Hebrew names.</summary>
    [HttpGet("operators")]
    [ProducesResponseType<IReadOnlyList<DataTypeOperatorsDto>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<DataTypeOperatorsDto>> GetOperators(CancellationToken ct) =>
        (await catalog.GetOperatorsAsync(ct)).Select(DataTypeOperatorsDto.From).ToList();

    /// <summary>Reloads the catalog from the database (e.g. after an entity was added to meta).</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Refresh()
    {
        metadata.Invalidate();
        return NoContent();
    }
}
