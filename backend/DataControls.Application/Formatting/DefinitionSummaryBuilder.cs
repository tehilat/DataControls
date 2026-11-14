using DataControls.Application.Binding;
using DataControls.Domain.Definition;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Formatting;

/// <summary>
/// A Hebrew, one-sentence summary of what a (bound) definition finds, for a user who approves a control
/// they did not build themselves, e.g. "יאותרו משלוחים שבהם משקל חבילה גדול מ-כושר נשיאה (רחפן)."
/// Built only from catalog names, so it describes what will actually run — not what the AI says it does.
/// </summary>
public static class DefinitionSummaryBuilder
{
    public static string Build(BoundDefinition definition) =>
        $"יאותרו {definition.RootEntity.DisplayNamePlural} שבהם {Describe(definition.Condition).Text}.";

    private static (string Text, int Parts) Describe(BoundNode node)
    {
        switch (node)
        {
            case BoundRule rule:
                return (DescribeRule(rule), 1);

            case BoundGroup group:
            {
                var children = group.Children.Select(Describe).ToList();
                if (children.Count == 1) return children[0];

                var separator = group.Logic == LogicalOperator.And ? " וגם " : " או ";
                var parts = children.Select(c => c.Parts > 1 ? $"({c.Text})" : c.Text);
                return (string.Join(separator, parts), children.Count);
            }

            default:
                return (string.Empty, 0);
        }
    }

    private static string DescribeRule(BoundRule rule)
    {
        var name = rule.Field.DisplayName;
        var op = rule.OperatorDisplayName;

        if (rule.CompareTo is null)
            return $"{name} {op}"; // "טלפון ריק"

        var operand = rule.CompareTo switch
        {
            BoundValue v => v.DisplayText,
            BoundFieldOperand f => f.Field.DisplayName,
            BoundToday t => ValueFormatter.FormatToday(t.OffsetDays),
            BoundNow => ValueFormatter.NowText,
            _ => string.Empty
        };

        // pick-list / yes-no: "סטטוס רחפן: בשירות", "סטטוס משלוח אינו נמסר"
        if (rule.Field.Field.DataType is FieldDataType.Boolean or FieldDataType.Enum && rule.CompareTo is BoundValue)
            return rule.Operator == ComparisonOperator.Eq ? $"{name}: {operand}" : $"{name} {op} {operand}";

        return $"{name} {ValueFormatter.JoinOperator(op, operand)}";
    }
}
