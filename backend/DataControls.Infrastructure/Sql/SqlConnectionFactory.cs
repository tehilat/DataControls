using Microsoft.Data.SqlClient;

namespace DataControls.Infrastructure.Sql;

public interface ISqlConnectionFactory
{
    SqlConnection Create();
}

public sealed class SqlConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    public SqlConnection Create() => new(connectionString);
}
