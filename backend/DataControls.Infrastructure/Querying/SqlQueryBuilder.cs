using System.Data;
using System.Text;
using DataControls.Application.Abstractions;
using DataControls.Application.Binding;
using DataControls.Application.Querying;
using DataControls.Domain.Definition;
using DataControls.Domain.Metadata;
using DataControls.Infrastructure.Sql;

namespace DataControls.Infrastructure.Querying;

/// <summary>
/// Translates a bound definition into a single parameterized SELECT:
/// <list type="bullet">
/// <item>identifiers only from the metadata whitelist, always through <see cref="SqlIdentifier.Quote"/>;</item>
/// <item>every value is a parameter (text always nvarchar);</item>
/// <item>LEFT JOIN per distinct relation path (many-to-one, so no row multiplication);</item>
/// <item>TOP (@__maxRows) with COUNT(*) OVER () for the real total;</item>
/// <item>per-rule value columns and flags so the reason for each record can be explained.</item>
/// </list>
/// </summary>
public sealed class SqlQueryBuilder : IQueryBuilder
{
    /// <summary>Text parameters use a fixed size so plans are reused; longer values fall back to nvarchar(max).</summary>
    public const int TextParameterSize = 4000;

    private const string RootAlias = "t0";

    public BuiltQuery Build(BoundDefinition definition, int maxRows)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);

        var ctx = new Context(definition);
        var root = definition.RootEntity;
        var pk = $"{RootAlias}.{SqlIdentifier.Quote(root.PrimaryKeyColumn)}";

        ctx.Parameters.Add(new QueryParameter(ResultAliases.MaxRowsParameter, maxRows, DbType.Int32));

        // rule predicates are rendered once and reused in WHERE and in the per-rule flags
        var predicates = definition.Rules.Select(r => Predicate(r, ctx)).ToList();
        var where = Render(definition.Condition, predicates);

        var select = new List<string>
        {
            $"{pk} AS {SqlIdentifier.Quote(ResultAliases.Id)}",
            $"{RootAlias}.{SqlIdentifier.Quote(root.DisplayColumn)} AS {SqlIdentifier.Quote(ResultAliases.Display)}",
            $"COUNT(*) OVER () AS {SqlIdentifier.Quote(ResultAliases.Total)}"
        };

        var columns = new List<ResultColumnBinding>();
        for (var i = 0; i < definition.ResultFields.Count; i++)
        {
            var field = definition.ResultFields[i];
            var alias = ResultAliases.Column(i);
            select.Add($"{RootAlias}.{SqlIdentifier.Quote(field.ColumnName)} AS {SqlIdentifier.Quote(alias)}");
            columns.Add(new ResultColumnBinding(alias, field));
        }

        foreach (var rule in definition.Rules)
        {
            select.Add($"{ctx.Column(rule.Field)} AS {SqlIdentifier.Quote(ResultAliases.RuleLeft(rule.Index))}");
            if (rule.CompareTo is BoundFieldOperand right)
                select.Add($"{ctx.Column(right.Field)} AS {SqlIdentifier.Quote(ResultAliases.RuleRight(rule.Index))}");
            select.Add($"CASE WHEN {predicates[rule.Index]} THEN 1 ELSE 0 END AS {SqlIdentifier.Quote(ResultAliases.RuleFlag(rule.Index))}");
        }

        var sql = new StringBuilder();
        sql.Append("SELECT TOP (").Append(ResultAliases.MaxRowsParameter).AppendLine(")");
        sql.AppendLine("    " + string.Join(",\n    ", select));
        sql.Append("FROM ").Append(Table(root)).Append(" AS ").AppendLine(RootAlias);

        foreach (var join in definition.Joins)
        {
            var alias = ctx.AliasOf(join);
            var parentAlias = ctx.AliasOf(join.Parent);
            sql.Append("LEFT JOIN ").Append(Table(join.Entity)).Append(" AS ").Append(alias)
               .Append(" ON ").Append(alias).Append('.').Append(SqlIdentifier.Quote(join.Relation.ToColumn))
               .Append(" = ").Append(parentAlias).Append('.').AppendLine(SqlIdentifier.Quote(join.Relation.FromColumn));
        }

        sql.Append("WHERE ").AppendLine(where);
        sql.Append("ORDER BY ").Append(pk).Append(';');

        return new BuiltQuery(sql.ToString(), ctx.Parameters, columns);
    }

    private static string Table(EntityMeta entity) =>
        $"{SqlIdentifier.Quote(entity.SchemaName)}.{SqlIdentifier.Quote(entity.TableName)}";

    private static string Render(BoundNode node, IReadOnlyList<string> predicates) => node switch
    {
        BoundRule rule => predicates[rule.Index],
        BoundGroup group => "(" + string.Join(
            group.Logic == LogicalOperator.And ? " AND " : " OR ",
            group.Children.Select(c => Render(c, predicates))) + ")",
        _ => throw new InvalidOperationException($"Unknown node type {node.GetType().Name}.")
    };

    private static string Predicate(BoundRule rule, Context ctx)
    {
        var left = ctx.Column(rule.Field);
        var isText = rule.Field.Field.DataType == FieldDataType.Text;

        switch (rule.Operator)
        {
            case ComparisonOperator.IsEmpty:
                // text: NULL, empty or whitespace-only all count as empty
                return isText ? $"({left} IS NULL OR TRIM({left}) = N'')" : $"{left} IS NULL";

            case ComparisonOperator.IsNotEmpty:
                return isText ? $"({left} IS NOT NULL AND TRIM({left}) <> N'')" : $"{left} IS NOT NULL";

            case ComparisonOperator.Contains:
            {
                if (rule.CompareTo is not BoundValue { Value: string text })
                    throw new InvalidOperationException("'contains' requires a text value.");
                var p = ctx.AddText("%" + EscapeLike(text) + "%");
                return $"{left} LIKE {p} ESCAPE N'\\'";
            }

            default:
                return $"{left} {SqlOperator(rule.Operator)} {Operand(rule, ctx)}";
        }
    }

    private static string Operand(BoundRule rule, Context ctx) => rule.CompareTo switch
    {
        BoundValue v => ctx.AddValue(v.Value, rule.Field.Field.DataType),
        BoundFieldOperand f => ctx.Column(f.Field),
        BoundToday t => $"DATEADD(DAY, {ctx.Add(t.OffsetDays, DbType.Int32)}, CAST(SYSDATETIME() AS DATE))",
        BoundNow => "SYSDATETIME()",
        null => throw new InvalidOperationException($"Operator {rule.Operator} requires a value."),
        _ => throw new InvalidOperationException($"Unknown operand type {rule.CompareTo.GetType().Name}.")
    };

    private static string SqlOperator(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Eq => "=",
        ComparisonOperator.Neq => "<>",
        ComparisonOperator.Gt => ">",
        ComparisonOperator.Gte => ">=",
        ComparisonOperator.Lt => "<",
        ComparisonOperator.Lte => "<=",
        _ => throw new InvalidOperationException($"Operator {op} is not a binary comparison.")
    };

    /// <summary>Escape LIKE wildcards so "contains" is a literal substring match.</summary>
    public static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[");

    private sealed class Context
    {
        private readonly Dictionary<BoundJoin, string> _aliases = new(ReferenceEqualityComparer.Instance);

        public Context(BoundDefinition definition)
        {
            for (var i = 0; i < definition.Joins.Count; i++)
                _aliases[definition.Joins[i]] = $"t{i + 1}";
        }

        public List<QueryParameter> Parameters { get; } = [];

        public string AliasOf(BoundJoin? join) =>
            join is null ? RootAlias
            : _aliases.TryGetValue(join, out var alias) ? alias
            : throw new InvalidOperationException($"Join '{join.Path}' is not part of the definition.");

        public string Column(BoundField field) => $"{AliasOf(field.Join)}.{SqlIdentifier.Quote(field.Field.ColumnName)}";

        public string Add(object value, DbType type, int? size = null)
        {
            var name = $"@p{Parameters.Count - 1}"; // @__maxRows is parameter #0
            Parameters.Add(new QueryParameter(name, value, type, size));
            return name;
        }

        /// <summary>Text is always nvarchar (DbType.String) — Hebrew must never go through varchar.</summary>
        public string AddText(string value) =>
            Add(value, DbType.String, value.Length <= TextParameterSize ? TextParameterSize : -1);

        public string AddValue(object value, FieldDataType dataType) => dataType switch
        {
            FieldDataType.Text or FieldDataType.Enum => AddText((string)value),
            FieldDataType.Number => Add(value, DbType.Decimal),
            FieldDataType.Date => Add(value, DbType.Date),
            FieldDataType.DateTime => Add(value, DbType.DateTime2),
            FieldDataType.Boolean => Add(value, DbType.Boolean),
            _ => throw new InvalidOperationException($"Unsupported data type {dataType}.")
        };
    }
}
