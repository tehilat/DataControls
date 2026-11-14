using DataControls.Application.Abstractions;
using DataControls.Application.Binding;
using DataControls.Application.Errors;
using DataControls.Domain.Definition;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Catalog;

// Business-only views of the catalog: keys, Hebrew names, types and options.
// Physical names (schema / table / column) never appear here — this is what the UI and the AI see.

public sealed record EntityView(string Key, string DisplayName, string DisplayNamePlural, string? Description);

public sealed record OptionView(string Value, string DisplayName);

/// <summary>A field reachable from a root entity, possibly through relations.</summary>
/// <param name="Path">Path as used in a definition, e.g. "Drone.Hub.IsActive".</param>
/// <param name="FullDisplayName">Same format as validation messages and reasons, e.g. "מוקד פעיל (רחפן › מוקד)".</param>
/// <param name="RelationPath">Hebrew names of the entities on the way, e.g. ["רחפן", "מוקד"]; empty for the root entity.</param>
public sealed record FieldPathView(
    string Path,
    string FieldKey,
    string DisplayName,
    string FullDisplayName,
    string? Description,
    string EntityKey,
    string EntityDisplayName,
    IReadOnlyList<string> RelationPath,
    FieldDataType DataType,
    string? Unit,
    IReadOnlyList<OptionView> Options);

public sealed record OperatorView(string Key, string DisplayName, bool RequiresValue, bool AllowFieldCompare);

public sealed record DataTypeOperatorsView(FieldDataType DataType, string DisplayName, IReadOnlyList<OperatorView> Operators);

public interface IBusinessCatalogService
{
    Task<IReadOnlyList<EntityView>> GetEntitiesAsync(CancellationToken ct = default);

    /// <param name="relationDepth">How many relations to follow (0 = only the entity's own fields).</param>
    Task<IReadOnlyList<FieldPathView>> GetFieldsAsync(string entityKey, int relationDepth, CancellationToken ct = default);

    Task<IReadOnlyList<DataTypeOperatorsView>> GetOperatorsAsync(CancellationToken ct = default);
}

public sealed class BusinessCatalogService(IMetadataProvider metadata) : IBusinessCatalogService
{
    public const int DefaultRelationDepth = 2;

    /// <summary>A path has at most <see cref="DefinitionLimits.MaxPathSegments"/> segments, the last one being the field.</summary>
    public const int MaxRelationDepth = DefinitionLimits.MaxPathSegments - 1;

    public async Task<IReadOnlyList<EntityView>> GetEntitiesAsync(CancellationToken ct = default)
    {
        var catalog = await metadata.GetCatalogAsync(ct);
        return catalog.Entities
            .OrderBy(e => e.DisplayName, StringComparer.Ordinal)
            .Select(e => new EntityView(e.EntityKey, e.DisplayName, e.DisplayNamePlural, e.Description))
            .ToList();
    }

    public async Task<IReadOnlyList<FieldPathView>> GetFieldsAsync(string entityKey, int relationDepth,
        CancellationToken ct = default)
    {
        if (relationDepth < 0 || relationDepth > MaxRelationDepth)
            throw new InvalidRequestException("depth", $"עומק הקשרים חייב להיות בין 0 ל-{MaxRelationDepth}.");

        var catalog = await metadata.GetCatalogAsync(ct);
        if (!catalog.TryGetEntity(entityKey, out var root))
            throw new EntityNotFoundException(entityKey);

        var result = new List<FieldPathView>();
        Walk(catalog, root, prefix: "", relationNames: [], visited: [root.EntityKey], relationDepth, result);
        return result;
    }

    public async Task<IReadOnlyList<DataTypeOperatorsView>> GetOperatorsAsync(CancellationToken ct = default)
    {
        var catalog = await metadata.GetCatalogAsync(ct);
        var operators = catalog.Operators.ToList(); // ordered by SortOrder

        return Enum.GetValues<FieldDataType>().Select(type =>
        {
            var allowed = catalog.GetOperatorsFor(type);
            var views = operators
                .Where(op => ComparisonOperators.TryParse(op.OperatorKey, out _)) // only what the engine can run
                .Select(op => (op, rule: allowed.FirstOrDefault(r =>
                    string.Equals(r.OperatorKey, op.OperatorKey, StringComparison.OrdinalIgnoreCase))))
                .Where(x => x.rule is not null)
                .Select(x => new OperatorView(x.op.OperatorKey, x.rule!.DisplayNameOverride ?? x.op.DisplayName,
                    x.op.RequiresValue, x.rule.AllowFieldCompare))
                .ToList();
            return new DataTypeOperatorsView(type, type.HebrewName(), views);
        }).ToList();
    }

    /// <summary>Own fields first, then each relation (depth-first). An entity already on the path is not revisited.</summary>
    private static void Walk(MetadataCatalog catalog, EntityMeta entity, string prefix, IReadOnlyList<string> relationNames,
        IReadOnlyList<string> visited, int depthLeft, List<FieldPathView> result)
    {
        foreach (var field in catalog.GetFields(entity.EntityKey))
        {
            var options = field.DataType == FieldDataType.Enum
                ? catalog.GetOptions(field.FieldId).Select(o => new OptionView(o.OptionValue, o.DisplayName)).ToList()
                : [];

            result.Add(new FieldPathView(
                prefix + field.FieldKey,
                field.FieldKey,
                field.DisplayName,
                relationNames.Count == 0 ? field.DisplayName : $"{field.DisplayName} ({string.Join(" › ", relationNames)})",
                field.Description,
                entity.EntityKey,
                entity.DisplayName,
                relationNames,
                field.DataType,
                field.Unit,
                options));
        }

        if (depthLeft == 0) return;

        foreach (var relation in catalog.GetRelations(entity.EntityKey).OrderBy(r => r.RelationKey, StringComparer.Ordinal))
        {
            if (!catalog.TryGetEntity(relation.ToEntityKey, out var target)) continue;
            if (visited.Contains(target.EntityKey, StringComparer.OrdinalIgnoreCase)) continue;

            Walk(catalog, target, prefix + relation.RelationKey + ".", [.. relationNames, target.DisplayName],
                [.. visited, target.EntityKey], depthLeft - 1, result);
        }
    }
}
