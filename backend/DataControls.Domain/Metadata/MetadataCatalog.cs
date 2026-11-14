namespace DataControls.Domain.Metadata;

/// <summary>
/// Immutable, indexed snapshot of the business catalog (meta.*).
/// Keys are matched case-insensitively; the canonical key from meta is what the engine uses afterwards.
/// </summary>
public sealed class MetadataCatalog
{
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, EntityMeta> _entities;
    private readonly Dictionary<string, Dictionary<string, FieldMeta>> _fieldsByEntity;
    private readonly Dictionary<int, IReadOnlyList<FieldOptionMeta>> _optionsByField;
    private readonly Dictionary<string, Dictionary<string, RelationMeta>> _relationsByEntity;
    private readonly Dictionary<string, OperatorMeta> _operators;
    private readonly Dictionary<(FieldDataType, string), DataTypeOperatorMeta> _dataTypeOperators;

    public MetadataCatalog(
        IEnumerable<EntityMeta> entities,
        IEnumerable<FieldMeta> fields,
        IEnumerable<FieldOptionMeta> options,
        IEnumerable<RelationMeta> relations,
        IEnumerable<OperatorMeta> operators,
        IEnumerable<DataTypeOperatorMeta> dataTypeOperators)
    {
        _entities = entities.ToDictionary(e => e.EntityKey, KeyComparer);

        _fieldsByEntity = fields
            .Where(f => _entities.ContainsKey(f.EntityKey))
            .GroupBy(f => f.EntityKey, KeyComparer)
            .ToDictionary(g => g.Key, g => g.ToDictionary(f => f.FieldKey, KeyComparer), KeyComparer);

        _optionsByField = options
            .GroupBy(o => o.FieldId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<FieldOptionMeta>)g.OrderBy(o => o.SortOrder).ToList());

        _relationsByEntity = relations
            .Where(r => _entities.ContainsKey(r.FromEntityKey) && _entities.ContainsKey(r.ToEntityKey))
            .GroupBy(r => r.FromEntityKey, KeyComparer)
            .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.RelationKey, KeyComparer), KeyComparer);

        _operators = operators.ToDictionary(o => o.OperatorKey, KeyComparer);

        _dataTypeOperators = dataTypeOperators
            .Where(d => _operators.ContainsKey(d.OperatorKey))
            .ToDictionary(d => (d.DataType, d.OperatorKey.ToLowerInvariant()));
    }

    public IEnumerable<EntityMeta> Entities => _entities.Values;
    public IEnumerable<OperatorMeta> Operators => _operators.Values.OrderBy(o => o.SortOrder);

    public bool TryGetEntity(string? key, out EntityMeta entity) =>
        _entities.TryGetValue(key ?? string.Empty, out entity!);

    public IReadOnlyList<FieldMeta> GetFields(string entityKey) =>
        _fieldsByEntity.TryGetValue(entityKey, out var fields)
            ? fields.Values.OrderBy(f => f.SortOrder).ToList()
            : [];

    public bool TryGetField(string entityKey, string fieldKey, out FieldMeta field)
    {
        field = null!;
        return _fieldsByEntity.TryGetValue(entityKey, out var fields) && fields.TryGetValue(fieldKey, out field!);
    }

    public IReadOnlyList<RelationMeta> GetRelations(string entityKey) =>
        _relationsByEntity.TryGetValue(entityKey, out var relations) ? relations.Values.ToList() : [];

    public bool TryGetRelation(string fromEntityKey, string relationKey, out RelationMeta relation)
    {
        relation = null!;
        return _relationsByEntity.TryGetValue(fromEntityKey, out var relations)
               && relations.TryGetValue(relationKey, out relation!);
    }

    public IReadOnlyList<FieldOptionMeta> GetOptions(int fieldId) =>
        _optionsByField.TryGetValue(fieldId, out var options) ? options : [];

    public bool TryGetOperator(string? key, out OperatorMeta op) =>
        _operators.TryGetValue(key ?? string.Empty, out op!);

    public bool TryGetDataTypeOperator(FieldDataType dataType, string operatorKey, out DataTypeOperatorMeta rule) =>
        _dataTypeOperators.TryGetValue((dataType, operatorKey.ToLowerInvariant()), out rule!);

    public IReadOnlyList<DataTypeOperatorMeta> GetOperatorsFor(FieldDataType dataType) =>
        _dataTypeOperators.Values.Where(d => d.DataType == dataType).ToList();
}
