using DataControls.Infrastructure.Querying;

namespace DataControls.Infrastructure.Sql;

/// <summary>Helpers for text that travels as a parameter into LIKE (always with ESCAPE N'\').</summary>
public static class SqlText
{
    /// <summary>A literal substring pattern: "%" + escaped text + "%".</summary>
    public static string Contains(string text) => "%" + SqlQueryBuilder.EscapeLike(text) + "%";
}
