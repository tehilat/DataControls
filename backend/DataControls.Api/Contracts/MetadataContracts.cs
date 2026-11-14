using DataControls.Application.Catalog;
using DataControls.Domain.Metadata;

namespace DataControls.Api.Contracts;

// The catalog in business terms only. There are no schema / table / column names in these contracts.

/// <summary>A business entity a control can be defined on.</summary>
public sealed record EntityDto(string Key, string DisplayName, string DisplayNamePlural, string? Description)
{
    public static EntityDto From(EntityView e) => new(e.Key, e.DisplayName, e.DisplayNamePlural, e.Description);
}

/// <summary>A value of a list (enum) field.</summary>
public sealed record OptionDto(string Value, string DisplayName);

/// <summary>A field reachable from the requested entity, possibly through relations.</summary>
/// <param name="Path">The value to put in a condition's "field", e.g. "Drone.Hub.IsActive".</param>
/// <param name="FullDisplayName">Hebrew name including the relation chain, e.g. "מוקד פעיל (רחפן › מוקד)".</param>
/// <param name="RelationPath">Hebrew names of the related entities on the way; empty for the entity's own fields.</param>
/// <param name="DataType">number | text | date | datetime | boolean | enum</param>
public sealed record FieldDto(
    string Path,
    string FieldKey,
    string DisplayName,
    string FullDisplayName,
    string? Description,
    string EntityKey,
    string EntityDisplayName,
    IReadOnlyList<string> RelationPath,
    string DataType,
    string? Unit,
    IReadOnlyList<OptionDto> Options)
{
    public static FieldDto From(FieldPathView f) => new(f.Path, f.FieldKey, f.DisplayName, f.FullDisplayName, f.Description,
        f.EntityKey, f.EntityDisplayName, f.RelationPath, f.DataType.ToKey(), f.Unit,
        f.Options.Select(o => new OptionDto(o.Value, o.DisplayName)).ToList());
}

/// <param name="RequiresValue">false for operators such as "ריק" that take no compareTo.</param>
/// <param name="AllowFieldCompare">Whether compareTo may be another field.</param>
public sealed record OperatorDto(string Key, string DisplayName, bool RequiresValue, bool AllowFieldCompare);

/// <summary>The operators allowed for one data type.</summary>
public sealed record DataTypeOperatorsDto(string DataType, string DisplayName, IReadOnlyList<OperatorDto> Operators)
{
    public static DataTypeOperatorsDto From(DataTypeOperatorsView v) => new(v.DataType.ToKey(), v.DisplayName,
        v.Operators.Select(o => new OperatorDto(o.Key, o.DisplayName, o.RequiresValue, o.AllowFieldCompare)).ToList());
}
