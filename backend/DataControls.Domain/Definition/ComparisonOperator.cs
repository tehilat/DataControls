namespace DataControls.Domain.Definition;

/// <summary>
/// Operators the engine knows how to translate. Which of them a business user may pick,
/// and for which data type, is decided by meta.Operators / meta.DataTypeOperators.
/// </summary>
public enum ComparisonOperator
{
    Eq, Neq, Gt, Gte, Lt, Lte, Contains, IsEmpty, IsNotEmpty
}

public static class ComparisonOperators
{
    private static readonly Dictionary<string, ComparisonOperator> ByKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eq"] = ComparisonOperator.Eq,
        ["neq"] = ComparisonOperator.Neq,
        ["gt"] = ComparisonOperator.Gt,
        ["gte"] = ComparisonOperator.Gte,
        ["lt"] = ComparisonOperator.Lt,
        ["lte"] = ComparisonOperator.Lte,
        ["contains"] = ComparisonOperator.Contains,
        ["isEmpty"] = ComparisonOperator.IsEmpty,
        ["isNotEmpty"] = ComparisonOperator.IsNotEmpty
    };

    public static bool TryParse(string? key, out ComparisonOperator op)
    {
        op = default;
        return key is not null && ByKey.TryGetValue(key, out op);
    }
}
