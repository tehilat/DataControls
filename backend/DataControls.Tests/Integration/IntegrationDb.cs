using DataControls.Application.Binding;
using DataControls.Application.Execution;
using DataControls.Application.Results;
using DataControls.Infrastructure.Execution;
using DataControls.Infrastructure.Metadata;
using DataControls.Infrastructure.Persistence;
using DataControls.Infrastructure.Querying;
using DataControls.Infrastructure.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DataControls.Tests.Integration;

/// <summary>
/// Access to the local DroneOpsControls database (scripts 01–05 applied).
/// Override with env DATACONTROLS_TEST_DB. Tests are skipped when the database is unreachable.
/// </summary>
public static class IntegrationDb
{
    private const string DefaultConnectionString =
        @"Server=localhost\SQLEXPRESS;Database=DroneOpsControls;Trusted_Connection=True;TrustServerCertificate=True;";

    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("DATACONTROLS_TEST_DB") is { Length: > 0 } cs ? cs : DefaultConnectionString;

    private static readonly Lazy<string?> Unavailable = new(() =>
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(ConnectionString) { ConnectTimeout = 5 };
            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            using var command = new SqlCommand("SELECT COUNT(*) FROM ctl.Controls;", connection);
            command.ExecuteScalar();
            return null;
        }
        catch (Exception ex)
        {
            return $"Integration database not available: {ex.Message}";
        }
    });

    public static string? SkipReason => Unavailable.Value;

    public static ISqlConnectionFactory Connections { get; } = new SqlConnectionFactory(ConnectionString);

    public static ControlExecutionService CreateService(int maxRows = 1000, int timeoutSeconds = 30)
    {
        var execution = Options.Create(new ControlExecutionOptions { MaxRows = maxRows, CommandTimeoutSeconds = timeoutSeconds });
        return new ControlExecutionService(
            new SqlControlRepository(Connections),
            new SqlControlRunRepository(Connections),
            CreateMetadataProvider(),
            new DefinitionBinder(),
            new SqlQueryBuilder(),
            CreateRunner(timeoutSeconds),
            new ViolationReasonBuilder(),
            execution,
            NullLogger<ControlExecutionService>.Instance);
    }

    public static SqlMetadataProvider CreateMetadataProvider() =>
        new(Connections, new MemoryCache(new MemoryCacheOptions()), Options.Create(new MetadataOptions()),
            NullLogger<SqlMetadataProvider>.Instance);

    public static SqlControlRunner CreateRunner(int timeoutSeconds = 30) =>
        new(Connections, Options.Create(new ControlExecutionOptions { CommandTimeoutSeconds = timeoutSeconds }),
            NullLogger<SqlControlRunner>.Instance);

    /// <summary>Runs a hand-written statement as the application (dbo) — for setup and cleanup only.</summary>
    public static async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = Connections.Create();
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Removes a control created by a test, with its runs and their stored results (cascade).</summary>
    public static Task DeleteControlAsync(int controlId) => ExecuteAsync("""
        DELETE FROM ctl.ControlRuns WHERE ControlId = @id;
        DELETE FROM ctl.Controls WHERE ControlId = @id;
        """, ("@id", controlId));

    /// <summary>Runs a hand-written query as the application (dbo) — for expectations and setup only.</summary>
    public static async Task<List<T>> QueryAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = Connections.Create();
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<T>();
        while (await reader.ReadAsync()) result.Add((T)reader.GetValue(0));
        return result;
    }
}

public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (IntegrationDb.SkipReason is { } reason) Skip = reason;
    }
}

public sealed class IntegrationTheoryAttribute : TheoryAttribute
{
    public IntegrationTheoryAttribute()
    {
        if (IntegrationDb.SkipReason is { } reason) Skip = reason;
    }
}
