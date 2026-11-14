namespace DataControls.Domain.Metadata;

public enum FieldDataType
{
    Number, Text, Date, DateTime, Boolean, Enum
}

public static class FieldDataTypes
{
    public static bool TryParse(string? value, out FieldDataType type)
    {
        type = value switch
        {
            "number" => FieldDataType.Number,
            "text" => FieldDataType.Text,
            "date" => FieldDataType.Date,
            "datetime" => FieldDataType.DateTime,
            "boolean" => FieldDataType.Boolean,
            "enum" => FieldDataType.Enum,
            _ => (FieldDataType)(-1)
        };
        return Enum.IsDefined(type);
    }

    public static string HebrewName(this FieldDataType type) => type switch
    {
        FieldDataType.Number => "מספר",
        FieldDataType.Text => "טקסט",
        FieldDataType.Date => "תאריך",
        FieldDataType.DateTime => "תאריך ושעה",
        FieldDataType.Boolean => "כן/לא",
        FieldDataType.Enum => "רשימת ערכים",
        _ => type.ToString()
    };

    public static bool IsTemporal(this FieldDataType type) =>
        type is FieldDataType.Date or FieldDataType.DateTime;
}

/// <summary>A business entity (meta.Entities). Physical names are a whitelist for the query builder.</summary>
public sealed record EntityMeta(
    string EntityKey,
    string DisplayName,
    string DisplayNamePlural,
    string? Description,
    string SchemaName,
    string TableName,
    string PrimaryKeyColumn,
    string DisplayColumn);

/// <summary>A business field (meta.Fields) together with the physical column facts.</summary>
public sealed record FieldMeta(
    int FieldId,
    string EntityKey,
    string FieldKey,
    string DisplayName,
    string? Description,
    string ColumnName,
    FieldDataType DataType,
    string? Unit,
    bool ShowInResults,
    int SortOrder,
    ColumnInfo Column);

public sealed record FieldOptionMeta(int FieldId, string OptionValue, string DisplayName, int SortOrder);

/// <summary>Many-to-one relation (child -> parent), so joins never multiply root rows.</summary>
public sealed record RelationMeta(
    string FromEntityKey,
    string RelationKey,
    string ToEntityKey,
    string FromColumn,
    string ToColumn,
    string DisplayName);

public sealed record OperatorMeta(string OperatorKey, string DisplayName, bool RequiresValue, int SortOrder);

public sealed record DataTypeOperatorMeta(
    FieldDataType DataType,
    string OperatorKey,
    string? DisplayNameOverride,
    bool AllowFieldCompare);
