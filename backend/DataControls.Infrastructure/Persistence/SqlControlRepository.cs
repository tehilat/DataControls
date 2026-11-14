using Dapper;
using DataControls.Application.Abstractions;
using DataControls.Domain.Controls;
using DataControls.Infrastructure.Sql;

namespace DataControls.Infrastructure.Persistence;

/// <summary>Read access to ctl.Controls (the system's own table, not business data).</summary>
public sealed class SqlControlRepository(ISqlConnectionFactory db) : IControlRepository
{
    private const string SelectColumns = """
        SELECT ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source, IsActive, CreatedAt, UpdatedAt
        FROM ctl.Controls
        """;

    public async Task<Control?> GetByIdAsync(int controlId, CancellationToken ct = default)
    {
        await using var connection = db.Create();
        var row = await connection.QuerySingleOrDefaultAsync<ControlRow>(
            new CommandDefinition(SelectColumns + " WHERE ControlId = @controlId;", new { controlId }, cancellationToken: ct));
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<Control>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = db.Create();
        var rows = await connection.QueryAsync<ControlRow>(
            new CommandDefinition(SelectColumns + " ORDER BY ControlId;", cancellationToken: ct));
        return rows.Select(r => r.ToDomain()).ToList();
    }

    private sealed class ControlRow
    {
        public int ControlId { get; init; }
        public string Name { get; init; } = "";
        public string? Description { get; init; }
        public string RootEntityKey { get; init; } = "";
        public string DefinitionJson { get; init; } = "";
        public string Severity { get; init; } = "";
        public string Source { get; init; } = "";
        public bool IsActive { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }

        public Control ToDomain() => new()
        {
            ControlId = ControlId,
            Name = Name,
            Description = Description,
            RootEntityKey = RootEntityKey,
            DefinitionJson = DefinitionJson,
            Severity = Enum.Parse<ControlSeverity>(Severity, ignoreCase: true),
            Source = Enum.Parse<ControlSource>(Source, ignoreCase: true),
            IsActive = IsActive,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }
}
