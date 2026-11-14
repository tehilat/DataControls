using System.Text.RegularExpressions;

namespace DataControls.Tests.Support;

/// <summary>
/// Reads test expectations straight from /database so there is a single source of truth:
/// definitions from 04_sample_controls.sql, expected ids from 99_verify.sql.
/// </summary>
public static partial class DatabaseScripts
{
    public static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "database", "99_verify.sql")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Repository root (with database/99_verify.sql) not found.");
    }

    private static string Read(string fileName) => File.ReadAllText(Path.Combine(RepoRoot, "database", fileName));

    [GeneratedRegex(@"INSERT INTO @expected VALUES(?<body>.*?);", RegexOptions.Singleline)]
    private static partial Regex ExpectedBlock();

    [GeneratedRegex(@"\((?<n>\d+),\s*N'(?<ids>[\d,]*)'\)")]
    private static partial Regex ExpectedRow();

    /// <summary>Control number → expected violating ids (sorted), from 99_verify.sql.</summary>
    public static IReadOnlyDictionary<int, int[]> ExpectedViolations()
    {
        var block = ExpectedBlock().Match(Read("99_verify.sql"));
        if (!block.Success) throw new InvalidOperationException("@expected block not found in 99_verify.sql.");

        return ExpectedRow().Matches(block.Groups["body"].Value).ToDictionary(
            m => int.Parse(m.Groups["n"].Value),
            m => m.Groups["ids"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).Order().ToArray());
    }

    [GeneratedRegex(@"\((?<n>\d+),\s*N'[^']*',\s*N'[^']*',\s*N'\w+',\s*N'(?<json>\{.*?\})',", RegexOptions.Singleline)]
    private static partial Regex ControlInsert();

    /// <summary>Control number → DefinitionJson, from 04_sample_controls.sql.</summary>
    public static IReadOnlyDictionary<int, string> SampleDefinitions() =>
        ControlInsert().Matches(Read("04_sample_controls.sql"))
            .ToDictionary(m => int.Parse(m.Groups["n"].Value), m => m.Groups["json"].Value);

    public static TheoryData<int> ControlNumbers()
    {
        var data = new TheoryData<int>();
        foreach (var n in ExpectedViolations().Keys.Order()) data.Add(n);
        return data;
    }
}
