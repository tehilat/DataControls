using System.Data;
using DataControls.Application.Abstractions;
using DataControls.Application.Errors;
using DataControls.Application.Execution;
using DataControls.Application.Querying;
using DataControls.Infrastructure.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataControls.Infrastructure.Execution;

/// <summary>
/// Executes a generated query with least privilege:
/// <c>EXECUTE AS USER = 'controls_runner'</c> (SELECT on ops only) → query (timeout, TOP) → <c>REVERT</c>.
/// If the session cannot be verified as reverted, the connection pool is cleared so an impersonated
/// connection is never reused.
/// </summary>
public sealed class SqlControlRunner(
    ISqlConnectionFactory db,
    IOptions<ControlExecutionOptions> options,
    ILogger<SqlControlRunner> logger) : IControlRunner
{
    private const int SqlTimeoutErrorNumber = -2;

    public async Task<RawQueryResult> RunAsync(BuiltQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var opts = options.Value;
        if (!SqlIdentifier.IsValid(opts.RunnerUser))
            throw new InvalidOperationException("ControlExecution:RunnerUser is not a valid user name.");

        await using var connection = db.Create();
        await connection.OpenAsync(ct);

        var impersonating = false;
        try
        {
            await using (var impersonate = connection.CreateCommand())
            {
                impersonate.CommandText = "EXECUTE AS USER = @runner;";
                impersonate.Parameters.Add(new SqlParameter("@runner", SqlDbType.NVarChar, 128) { Value = opts.RunnerUser });
                await impersonate.ExecuteNonQueryAsync(ct);
            }
            impersonating = true;

            return await ReadAsync(connection, query, opts.CommandTimeoutSeconds, ct);
        }
        catch (SqlException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (SqlException ex) when (ex.Number == SqlTimeoutErrorNumber)
        {
            throw new ControlExecutionException(
                $"הרצת הבקרה חרגה מזמן ההמתנה המותר ({opts.CommandTimeoutSeconds} שניות).", ex);
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Control query failed (SQL error {Number})", ex.Number);
            throw new ControlExecutionException("אירעה שגיאה בהרצת הבקרה מול מסד הנתונים.", ex);
        }
        finally
        {
            if (impersonating) await RevertAsync(connection, opts.RunnerUser);
        }
    }

    private static async Task<RawQueryResult> ReadAsync(SqlConnection connection, BuiltQuery query, int timeoutSeconds,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = query.Sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = timeoutSeconds;

        foreach (var p in query.Parameters)
        {
            var parameter = new SqlParameter(p.Name, p.Value ?? DBNull.Value) { DbType = p.DbType };
            if (p.Size is { } size) parameter.Size = size;
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);
        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var rows = new List<IReadOnlyDictionary<string, object?>>();

        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(names.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < names.Length; i++)
                row[names[i]] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return new RawQueryResult(rows);
    }

    private async Task RevertAsync(SqlConnection connection, string runnerUser)
    {
        try
        {
            if (connection.State == ConnectionState.Open)
            {
                await using var revert = connection.CreateCommand();
                revert.CommandText = "REVERT; SELECT USER_NAME();";
                var current = (string?)await revert.ExecuteScalarAsync(CancellationToken.None);
                if (!string.Equals(current, runnerUser, StringComparison.OrdinalIgnoreCase))
                    return; // back to the application's own context
            }
            logger.LogWarning("Could not verify REVERT of the runner context; clearing the connection pool.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "REVERT failed; clearing the connection pool.");
        }

        SqlConnection.ClearPool(connection);
    }
}
