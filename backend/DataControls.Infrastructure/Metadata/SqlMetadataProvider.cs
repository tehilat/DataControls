using Dapper;
using DataControls.Application.Abstractions;
using DataControls.Domain.Metadata;
using DataControls.Infrastructure.Sql;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataControls.Infrastructure.Metadata;

/// <summary>Configuration section "Metadata".</summary>
public sealed class MetadataOptions
{
    public const string SectionName = "Metadata";

    /// <summary>Only entities in these schemas are exposed (business data lives in ops).</summary>
    public string[] AllowedSchemas { get; set; } = ["ops"];

    public int CacheSeconds { get; set; } = 300;
}

/// <summary>Loads meta.* (and the physical column facts) with Dapper; caches the catalog.</summary>
public sealed class SqlMetadataProvider(
    ISqlConnectionFactory db,
    IMemoryCache cache,
    IOptions<MetadataOptions> options,
    ILogger<SqlMetadataProvider> logger) : IMetadataProvider
{
    private const string CacheKey = "DataControls.MetadataCatalog";

    private const string Sql = """
        SELECT EntityKey, DisplayName, DisplayNamePlural, Description, SchemaName, TableName, PrimaryKeyColumn, DisplayColumn
        FROM meta.Entities WHERE IsActive = 1;

        SELECT FieldId, EntityKey, FieldKey, DisplayName, Description, ColumnName, DataType, Unit, ShowInResults, SortOrder
        FROM meta.Fields WHERE IsActive = 1;

        SELECT FieldId, OptionValue, DisplayName, SortOrder FROM meta.FieldOptions;

        SELECT FromEntityKey, RelationKey, ToEntityKey, FromColumn, ToColumn, DisplayName FROM meta.Relations;

        SELECT OperatorKey, DisplayName, RequiresValue, SortOrder FROM meta.Operators;

        SELECT DataType, OperatorKey, DisplayNameOverride, AllowFieldCompare FROM meta.DataTypeOperators;

        SELECT s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName, ty.name AS SqlType,
               c.max_length AS MaxLength, c.precision AS Precision, c.scale AS Scale, c.is_nullable AS IsNullable
        FROM sys.columns c
        JOIN sys.tables  t  ON t.object_id = c.object_id
        JOIN sys.schemas s  ON s.schema_id = t.schema_id
        JOIN sys.types   ty ON ty.user_type_id = c.user_type_id
        WHERE s.name IN @Schemas;
        """;

    public async Task<MetadataCatalog> GetCatalogAsync(CancellationToken ct = default)
    {
        var catalog = await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(1, options.Value.CacheSeconds));
            return await LoadAsync(ct);
        });
        return catalog!;
    }

    public void Invalidate() => cache.Remove(CacheKey);

    private async Task<MetadataCatalog> LoadAsync(CancellationToken ct)
    {
        await using var connection = db.Create();
        var command = new CommandDefinition(Sql, new { Schemas = options.Value.AllowedSchemas }, cancellationToken: ct);
        await using var grid = await connection.QueryMultipleAsync(command);

        var rows = new MetadataRows
        {
            Entities = (await grid.ReadAsync<EntityRow>()).AsList(),
            Fields = (await grid.ReadAsync<FieldRow>()).AsList(),
            Options = (await grid.ReadAsync<FieldOptionRow>()).AsList(),
            Relations = (await grid.ReadAsync<RelationRow>()).AsList(),
            Operators = (await grid.ReadAsync<OperatorRow>()).AsList(),
            DataTypeOperators = (await grid.ReadAsync<DataTypeOperatorRow>()).AsList(),
            Columns = (await grid.ReadAsync<PhysicalColumnRow>()).AsList()
        };

        var catalog = MetadataCatalogFactory.Create(rows, options.Value.AllowedSchemas, out var warnings);
        foreach (var warning in warnings)
            logger.LogWarning("Metadata entry ignored: {Warning}", warning);

        logger.LogInformation("Metadata catalog loaded: {Entities} entities", catalog.Entities.Count());
        return catalog;
    }
}
