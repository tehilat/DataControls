using System.Text.RegularExpressions;

namespace DataControls.Infrastructure.Sql;

/// <summary>
/// The only way an identifier gets into generated SQL. Identifiers come from the metadata whitelist,
/// and are additionally restricted to a plain-ASCII pattern and bracket-quoted (defense in depth).
/// </summary>
public static partial class SqlIdentifier
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafePattern();

    public static bool IsValid(string? name) => name is not null && SafePattern().IsMatch(name);

    /// <exception cref="InvalidOperationException">The identifier is not a safe plain identifier.</exception>
    public static string Quote(string name)
    {
        if (!IsValid(name))
            throw new InvalidOperationException($"Unsafe SQL identifier rejected: '{name}'.");
        return "[" + name.Replace("]", "]]") + "]";
    }
}
