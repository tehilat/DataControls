using DataControls.Domain.Definition;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Binding;

/// <summary>
/// A definition after it was validated and resolved against the metadata catalog.
/// Every field, relation and operator here is a catalog object — nothing comes from free text.
/// </summary>
public sealed record BoundDefinition(
    EntityMeta RootEntity,
    BoundNode Condition,
    IReadOnlyList<BoundJoin> Joins,   // parents always precede children
    IReadOnlyList<BoundRule> Rules,   // Rules[i].Index == i
    IReadOnlyList<FieldMeta> ResultFields); // root-entity fields shown in results (ShowInResults, by SortOrder)

/// <summary>A distinct relation path from the root entity, e.g. "Drone" or "Drone.Hub".</summary>
public sealed record BoundJoin(string Path, RelationMeta Relation, EntityMeta Entity, BoundJoin? Parent);

/// <summary>A resolved field path. <see cref="Join"/> is null for a field of the root entity.</summary>
public sealed record BoundField(string Path, FieldMeta Field, BoundJoin? Join, string DisplayName);

public abstract record BoundNode;

public sealed record BoundGroup(LogicalOperator Logic, IReadOnlyList<BoundNode> Children) : BoundNode;

public sealed record BoundRule(
    int Index,
    BoundField Field,
    ComparisonOperator Operator,
    string OperatorDisplayName,
    BoundOperand? CompareTo) : BoundNode;

public abstract record BoundOperand;

/// <summary>A literal already converted to the field's CLR type (decimal, string, DateTime, bool).</summary>
public sealed record BoundValue(object Value, string DisplayText) : BoundOperand;

public sealed record BoundFieldOperand(BoundField Field) : BoundOperand;

public sealed record BoundToday(int OffsetDays) : BoundOperand;

public sealed record BoundNow : BoundOperand;
