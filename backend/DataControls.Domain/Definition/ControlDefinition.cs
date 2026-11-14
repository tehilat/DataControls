using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataControls.Domain.Definition;

/// <summary>
/// A control definition as stored in ctl.Controls.DefinitionJson (version 1).
/// It references business keys only (EntityKey / RelationKey / FieldKey / OperatorKey) — never SQL.
/// A record is a violation when <see cref="Condition"/> evaluates to true.
/// </summary>
public sealed class ControlDefinition
{
    public const int CurrentVersion = 1;

    public int Version { get; init; }
    public string RootEntity { get; init; } = string.Empty;
    public ConditionNode? Condition { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type",
    UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(ConditionGroup), "group")]
[JsonDerivedType(typeof(ConditionRule), "rule")]
public abstract class ConditionNode;

public sealed class ConditionGroup : ConditionNode
{
    public LogicalOperator Logic { get; init; }
    public IReadOnlyList<ConditionNode?> Children { get; init; } = [];
}

public sealed class ConditionRule : ConditionNode
{
    /// <summary>Path: "FieldKey" or "RelationKey(.RelationKey)*.FieldKey".</summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>OperatorKey from meta.Operators.</summary>
    public string Operator { get; init; } = string.Empty;

    /// <summary>Omitted for operators that do not require a value (isEmpty / isNotEmpty).</summary>
    public Operand? CompareTo { get; init; }
}

[JsonConverter(typeof(LogicalOperatorConverter))]
public enum LogicalOperator { And, Or }

/// <summary>Reads "AND"/"OR" case-insensitively (numbers are rejected); always writes upper case.</summary>
public sealed class LogicalOperatorConverter : JsonConverter<LogicalOperator>
{
    public override LogicalOperator Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return text?.ToUpperInvariant() switch
        {
            "AND" => LogicalOperator.And,
            "OR" => LogicalOperator.Or,
            _ => throw new JsonException("logic must be \"AND\" or \"OR\".")
        };
    }

    public override void Write(Utf8JsonWriter writer, LogicalOperator value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value == LogicalOperator.And ? "AND" : "OR");
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind",
    UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(ValueOperand), "value")]
[JsonDerivedType(typeof(FieldOperand), "field")]
[JsonDerivedType(typeof(TodayOperand), "today")]
[JsonDerivedType(typeof(NowOperand), "now")]
public abstract class Operand;

/// <summary>A literal JSON scalar; converted to the field's type by the binder.</summary>
public sealed class ValueOperand : Operand
{
    public JsonElement Value { get; init; }
}

/// <summary>Another field (path) of the same record graph.</summary>
public sealed class FieldOperand : Operand
{
    public string Field { get; init; } = string.Empty;
}

/// <summary>The run day plus an offset in days.</summary>
public sealed class TodayOperand : Operand
{
    public int OffsetDays { get; init; }
}

/// <summary>The date and time of the run.</summary>
public sealed class NowOperand : Operand;
