using System.Globalization;
using DataControls.Application.Binding;
using DataControls.Application.Formatting;
using DataControls.Application.Querying;
using DataControls.Domain.Definition;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Results;

/// <summary>
/// Builds a Hebrew, human-readable reason for one violating record, from the condition tree and the
/// per-rule values/flags the query returned. AND → every condition is explained; OR → only those that held.
/// </summary>
public sealed class ViolationReasonBuilder
{
    public const string FallbackReason = "הרשומה עומדת בתנאי הבקרה.";

    public string Build(BoundDefinition definition, MetadataCatalog catalog, IReadOnlyDictionary<string, object?> row)
    {
        var (_, text, _) = Describe(definition.Condition, catalog, row);
        return string.IsNullOrWhiteSpace(text) ? FallbackReason : text;
    }

    /// <returns>Whether the node held, its text, and how many parts the text joins (for parentheses).</returns>
    private static (bool Holds, string Text, int Parts) Describe(BoundNode node, MetadataCatalog catalog,
        IReadOnlyDictionary<string, object?> row)
    {
        switch (node)
        {
            case BoundRule rule:
                return (RuleHolds(rule, row), DescribeRule(rule, catalog, row), 1);

            case BoundGroup group:
            {
                var children = group.Children.Select(c => Describe(c, catalog, row)).ToList();
                var holds = group.Logic == LogicalOperator.And ? children.All(c => c.Holds) : children.Any(c => c.Holds);

                var shown = group.Logic == LogicalOperator.And ? children : children.Where(c => c.Holds).ToList();
                if (shown.Count == 1) return (holds, shown[0].Text, shown[0].Parts);

                // a multi-part child needs parentheses only when it sits next to siblings
                var parts = shown.Select(c => c.Parts > 1 ? $"({c.Text})" : c.Text);
                var separator = group.Logic == LogicalOperator.And ? " וגם " : " או ";
                return (holds, string.Join(separator, parts), shown.Count);
            }

            default:
                return (false, string.Empty, 0);
        }
    }

    private static bool RuleHolds(BoundRule rule, IReadOnlyDictionary<string, object?> row) =>
        row.TryGetValue(ResultAliases.RuleFlag(rule.Index), out var flag)
        && flag is not null and not DBNull
        && Convert.ToInt32(flag, CultureInfo.InvariantCulture) == 1;

    private static string DescribeRule(BoundRule rule, MetadataCatalog catalog, IReadOnlyDictionary<string, object?> row)
    {
        var name = rule.Field.DisplayName;
        var actual = ValueFormatter.Format(Get(row, ResultAliases.RuleLeft(rule.Index)), rule.Field.Field, catalog);
        var op = rule.OperatorDisplayName;

        switch (rule.Operator)
        {
            case ComparisonOperator.IsEmpty:
                return $"{name} {op}";
            case ComparisonOperator.IsNotEmpty:
                return $"{name} {op} ({actual})";
        }

        var expected = rule.CompareTo switch
        {
            BoundValue v => v.DisplayText,
            BoundFieldOperand f =>
                $"{f.Field.DisplayName} ({ValueFormatter.Format(Get(row, ResultAliases.RuleRight(rule.Index)), f.Field.Field, catalog)})",
            BoundToday t => ValueFormatter.FormatToday(t.OffsetDays),
            BoundNow => ValueFormatter.NowText,
            _ => string.Empty
        };

        // For a pick-list / yes-no field the actual value says it all: "סטטוס רחפן (רחפן): בתחזוקה"
        if (rule.Field.Field.DataType is FieldDataType.Boolean or FieldDataType.Enum && rule.CompareTo is BoundValue)
            return rule.Operator == ComparisonOperator.Eq
                ? $"{name}: {actual}"
                : $"{name}: {actual} ({op} {expected})";

        return $"{name} ({actual}) {ValueFormatter.JoinOperator(op, expected)}";
    }

    private static object? Get(IReadOnlyDictionary<string, object?> row, string alias) =>
        row.TryGetValue(alias, out var value) && value is not DBNull ? value : null;
}
