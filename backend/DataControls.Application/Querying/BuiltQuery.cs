using System.Data;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Querying;

/// <summary>A value that travels to SQL Server only as a parameter.</summary>
public sealed record QueryParameter(string Name, object? Value, DbType DbType, int? Size = null);

/// <summary>A root-entity column shown in the results grid.</summary>
public sealed record ResultColumnBinding(string Alias, FieldMeta Field);

/// <summary>Generated SELECT + its parameters. The SQL text never contains user-supplied values.</summary>
public sealed record BuiltQuery(
    string Sql,
    IReadOnlyList<QueryParameter> Parameters,
    IReadOnlyList<ResultColumnBinding> Columns);

/// <summary>Rows as returned by the runner, keyed by the column aliases in <see cref="ResultAliases"/>.</summary>
public sealed record RawQueryResult(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

/// <summary>Column-alias contract between the query builder and the result mapper.</summary>
public static class ResultAliases
{
    public const string Id = "__id";
    public const string Display = "__display";
    public const string Total = "__total";
    public const string MaxRowsParameter = "@__maxRows";

    public static string Column(int index) => $"c{index}";
    public static string RuleFlag(int ruleIndex) => $"__r{ruleIndex}";
    public static string RuleLeft(int ruleIndex) => $"__f{ruleIndex}";
    public static string RuleRight(int ruleIndex) => $"__g{ruleIndex}";
}
