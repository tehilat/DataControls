namespace DataControls.Infrastructure.Metadata;

// Raw rows as read from meta.* and sys.columns, before validation.

public sealed class EntityRow
{
    public string EntityKey { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string DisplayNamePlural { get; init; } = "";
    public string? Description { get; init; }
    public string SchemaName { get; init; } = "";
    public string TableName { get; init; } = "";
    public string PrimaryKeyColumn { get; init; } = "";
    public string DisplayColumn { get; init; } = "";
}

public sealed class FieldRow
{
    public int FieldId { get; init; }
    public string EntityKey { get; init; } = "";
    public string FieldKey { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? Description { get; init; }
    public string ColumnName { get; init; } = "";
    public string DataType { get; init; } = "";
    public string? Unit { get; init; }
    public bool ShowInResults { get; init; }
    public int SortOrder { get; init; }
}

public sealed class FieldOptionRow
{
    public int FieldId { get; init; }
    public string OptionValue { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int SortOrder { get; init; }
}

public sealed class RelationRow
{
    public string FromEntityKey { get; init; } = "";
    public string RelationKey { get; init; } = "";
    public string ToEntityKey { get; init; } = "";
    public string FromColumn { get; init; } = "";
    public string ToColumn { get; init; } = "";
    public string DisplayName { get; init; } = "";
}

public sealed class OperatorRow
{
    public string OperatorKey { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool RequiresValue { get; init; }
    public int SortOrder { get; init; }
}

public sealed class DataTypeOperatorRow
{
    public string DataType { get; init; } = "";
    public string OperatorKey { get; init; } = "";
    public string? DisplayNameOverride { get; init; }
    public bool AllowFieldCompare { get; init; }
}

/// <summary>A physical column from sys.columns.</summary>
public sealed class PhysicalColumnRow
{
    public string SchemaName { get; init; } = "";
    public string TableName { get; init; } = "";
    public string ColumnName { get; init; } = "";
    public string SqlType { get; init; } = "";
    public short MaxLength { get; init; }
    public byte Precision { get; init; }
    public byte Scale { get; init; }
    public bool IsNullable { get; init; }
}

public sealed class MetadataRows
{
    public IReadOnlyList<EntityRow> Entities { get; init; } = [];
    public IReadOnlyList<FieldRow> Fields { get; init; } = [];
    public IReadOnlyList<FieldOptionRow> Options { get; init; } = [];
    public IReadOnlyList<RelationRow> Relations { get; init; } = [];
    public IReadOnlyList<OperatorRow> Operators { get; init; } = [];
    public IReadOnlyList<DataTypeOperatorRow> DataTypeOperators { get; init; } = [];
    public IReadOnlyList<PhysicalColumnRow> Columns { get; init; } = [];
}
