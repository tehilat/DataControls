using System.Data;
using DataControls.Application.Querying;
using DataControls.Infrastructure.Querying;
using static DataControls.Tests.Support.Defs;

namespace DataControls.Tests.Unit;

public class SqlQueryBuilderTests
{
    private static QueryParameter Param(BuiltQuery q, string name) => Assert.Single(q.Parameters, p => p.Name == name);

    [Fact]
    public void Simple_rule_produces_parameterized_select_with_top_and_total()
    {
        var q = Build(Json("Delivery", Rule("PackageWeightKg", "lt", Value("0"))), maxRows: 50);

        Assert.StartsWith("SELECT TOP (@__maxRows)", q.Sql);
        Assert.Contains("FROM [ops].[Deliveries] AS t0", q.Sql);
        Assert.Contains("WHERE t0.[PackageWeightKg] < @p0", q.Sql);
        Assert.Contains("COUNT(*) OVER () AS [__total]", q.Sql);
        Assert.Contains("t0.[DeliveryId] AS [__id]", q.Sql);
        Assert.EndsWith("ORDER BY t0.[DeliveryId];", q.Sql);
        Assert.DoesNotContain("JOIN", q.Sql);

        Assert.Equal(50, Param(q, "@__maxRows").Value);
        var p0 = Param(q, "@p0");
        Assert.Equal(DbType.Decimal, p0.DbType);
        Assert.Equal(0m, p0.Value);
    }

    [Fact]
    public void Only_select_is_generated()
    {
        foreach (var json in Support.DatabaseScripts.SampleDefinitions().Values)
        {
            var sql = Build(json).Sql;
            Assert.StartsWith("SELECT ", sql);
            Assert.Equal(1, sql.Count(c => c == ';'));
            Assert.DoesNotMatch(@"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|EXEC|EXECUTE|CREATE|GRANT|INTO)\b", sql);
        }
    }

    [Fact]
    public void Field_compare_across_a_relation_uses_left_join_from_metadata()
    {
        var q = Build(Json("Delivery", Rule("PackageWeightKg", "gt", Field("Drone.MaxPayloadKg"))));

        Assert.Contains("LEFT JOIN [ops].[Drones] AS t1 ON t1.[DroneId] = t0.[DroneId]", q.Sql);
        Assert.Contains("t0.[PackageWeightKg] > t1.[MaxPayloadKg]", q.Sql);
        Assert.Contains("t1.[MaxPayloadKg] AS [__g0]", q.Sql);
        Assert.Single(q.Parameters); // only @__maxRows
    }

    [Fact]
    public void Two_level_path_chains_joins()
    {
        var q = Build(Json("Delivery", Rule("Drone.Hub.IsActive", "eq", Value("false"))));

        Assert.Contains("LEFT JOIN [ops].[Drones] AS t1 ON t1.[DroneId] = t0.[DroneId]", q.Sql);
        Assert.Contains("LEFT JOIN [ops].[Hubs] AS t2 ON t2.[HubId] = t1.[HubId]", q.Sql);
        Assert.Contains("t2.[IsActive] = @p0", q.Sql);
        Assert.Equal(DbType.Boolean, Param(q, "@p0").DbType);
        Assert.Equal(false, Param(q, "@p0").Value);
    }

    [Fact]
    public void A_relation_used_by_several_rules_is_joined_once()
    {
        var q = Build(Json("Delivery", Group("OR",
            Rule("Drone.Status", "eq", Value("\"Maintenance\"")),
            Rule("PackageWeightKg", "gt", Field("Drone.MaxPayloadKg")),
            Rule("DistanceKm", "gt", Field("Drone.MaxRangeKm")))));

        Assert.Equal(1, CountOf(q.Sql, "LEFT JOIN [ops].[Drones]"));
    }

    [Fact]
    public void Nested_groups_are_parenthesized()
    {
        var q = Build(Support.DatabaseScripts.SampleDefinitions()[10]);

        Assert.Contains(
            "WHERE ((t0.[Status] = @p0 AND t0.[DeliveredAt] IS NULL) OR (t0.[Status] = @p1 AND t0.[DeliveredAt] IS NOT NULL))",
            q.Sql);
        Assert.Equal("Delivered", Param(q, "@p0").Value);
        Assert.Equal("Failed", Param(q, "@p1").Value);
    }

    [Theory]
    [InlineData("eq", "=")]
    [InlineData("neq", "<>")]
    [InlineData("gt", ">")]
    [InlineData("gte", ">=")]
    [InlineData("lt", "<")]
    [InlineData("lte", "<=")]
    public void Comparison_operators(string op, string sqlOp) =>
        Assert.Contains($"WHERE t0.[DistanceKm] {sqlOp} @p0",
            Build(Json("Delivery", Rule("DistanceKm", op, Value("20")))).Sql);

    [Theory]
    [InlineData("Phone", "isEmpty", "(t0.[Phone] IS NULL OR TRIM(t0.[Phone]) = N'')")]
    [InlineData("Phone", "isNotEmpty", "(t0.[Phone] IS NOT NULL AND TRIM(t0.[Phone]) <> N'')")]
    public void Text_emptiness_treats_whitespace_as_empty(string field, string op, string expected) =>
        Assert.Contains($"WHERE {expected}", Build(Json("Customer", Rule(field, op))).Sql);

    [Theory]
    [InlineData("DeliveredAt", "isEmpty", "t0.[DeliveredAt] IS NULL")]
    [InlineData("DeliveredAt", "isNotEmpty", "t0.[DeliveredAt] IS NOT NULL")]
    [InlineData("PackageWeightKg", "isEmpty", "t0.[PackageWeightKg] IS NULL")]
    public void Non_text_emptiness_is_null_check_without_trim(string field, string op, string expected)
    {
        var sql = Build(Json("Delivery", Rule(field, op))).Sql;
        Assert.Contains($"WHERE {expected}", sql);
        Assert.DoesNotContain("TRIM", sql);
    }

    [Fact]
    public void Today_with_offset_is_computed_by_the_server_with_an_int_parameter()
    {
        var q = Build(Json("Drone", Rule("NextMaintenanceDue", "lt", Today(-7))));
        Assert.Contains("t0.[NextMaintenanceDue] < DATEADD(DAY, @p0, CAST(SYSDATETIME() AS DATE))", q.Sql);
        Assert.Equal(DbType.Int32, Param(q, "@p0").DbType);
        Assert.Equal(-7, Param(q, "@p0").Value);
    }

    [Fact]
    public void Now_is_server_time() =>
        Assert.Contains("t0.[DispatchedAt] > SYSDATETIME()", Build(Json("Delivery", Rule("DispatchedAt", "gt", Now))).Sql);

    [Fact]
    public void Date_and_datetime_values_have_proper_db_types()
    {
        var q = Build(Json("Delivery", Group("AND",
            Rule("DispatchedAt", "gte", Value("\"2026-01-01T08:30\"")),
            Rule("Drone.NextMaintenanceDue", "lt", Value("\"2026-02-01\"")))));

        Assert.Equal(DbType.DateTime2, Param(q, "@p0").DbType);
        Assert.Equal(new DateTime(2026, 1, 1, 8, 30, 0), Param(q, "@p0").Value);
        Assert.Equal(DbType.Date, Param(q, "@p1").DbType);
        Assert.Equal(new DateTime(2026, 2, 1), Param(q, "@p1").Value);
    }

    // ---------------- text parameters: always nvarchar, Hebrew safe ----------------

    [Fact]
    public void Hebrew_eq_value_is_an_nvarchar_parameter()
    {
        var q = Build(Json("Customer", Rule("City", "eq", Value("\"תל אביב\""))));

        Assert.Contains("WHERE t0.[City] = @p0", q.Sql);
        Assert.DoesNotContain("תל אביב", q.Sql);
        var p = Param(q, "@p0");
        Assert.Equal(DbType.String, p.DbType);
        Assert.Equal(SqlQueryBuilder.TextParameterSize, p.Size);
        Assert.Equal("תל אביב", p.Value);
    }

    [Fact]
    public void Hebrew_contains_value_is_an_escaped_nvarchar_like_pattern()
    {
        var q = Build(Json("Delivery", Rule("Customer.Address", "contains", Value("\"הרצל\""))));

        Assert.Contains("LEFT JOIN [ops].[Customers] AS t1 ON t1.[CustomerId] = t0.[CustomerId]", q.Sql);
        Assert.Contains("t1.[Address] LIKE @p0 ESCAPE N'\\'", q.Sql);
        Assert.DoesNotContain("הרצל", q.Sql);
        var p = Param(q, "@p0");
        Assert.Equal(DbType.String, p.DbType);
        Assert.Equal("%הרצל%", p.Value);
    }

    [Fact]
    public void Enum_values_are_nvarchar_too() =>
        Assert.Equal(DbType.String, Param(Build(Json("Drone", Rule("Status", "eq", Value("\"Active\"")))), "@p0").DbType);

    [Fact]
    public void Contains_escapes_like_wildcards()
    {
        var q = Build(Json("Customer", Rule("Email", "contains", Value(@"""50%_off[1]\\"""))));
        Assert.Equal(@"%50\%\_off\[1]\\%", Param(q, "@p0").Value);
    }

    [Fact]
    public void Result_columns_are_root_fields_marked_show_in_results()
    {
        var q = Build(Json("Drone", Rule("Status", "eq", Value("\"Active\""))));
        Assert.Equal(["SerialNumber", "Model", "MaxPayloadKg", "MaxRangeKm", "LastMaintenance", "NextMaintenanceDue", "Status"],
            q.Columns.Select(c => c.Field.FieldKey));
        Assert.Contains("t0.[SerialNumber] AS [__display]", q.Sql);
    }

    [Fact]
    public void Each_rule_gets_a_flag_column_for_reasons()
    {
        var q = Build(Support.DatabaseScripts.SampleDefinitions()[4]);
        Assert.Contains("CASE WHEN (t1.[LicenseNumber] IS NULL OR TRIM(t1.[LicenseNumber]) = N'') THEN 1 ELSE 0 END AS [__r0]", q.Sql);
        Assert.Contains("CASE WHEN t1.[LicenseExpiry] < t0.[DispatchedAt] THEN 1 ELSE 0 END AS [__r1]", q.Sql);
    }

    [Fact]
    public void MaxRows_must_be_positive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(Json("Customer", Rule("Phone", "isEmpty")), maxRows: 0));

    private static int CountOf(string text, string fragment) =>
        (text.Length - text.Replace(fragment, "").Length) / fragment.Length;
}
