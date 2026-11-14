namespace DataControls.Domain.Metadata;

public sealed record NumericRange(decimal Min, decimal Max);

public sealed record DateTimeRange(DateTime Min, DateTime Max);

/// <summary>
/// Physical column facts read from the SQL Server catalog (sys.columns).
/// Used to validate values before they ever reach SQL (e.g. a number that does not fit DECIMAL(6,2)).
/// </summary>
public sealed record ColumnInfo(string SqlType, int MaxLength, byte Precision, byte Scale, bool IsNullable)
{
    /// <summary>Column facts unknown (e.g. in-memory catalogs) — no range validation.</summary>
    public static readonly ColumnInfo Unknown = new("unknown", 0, 0, 0, true);

    /// <summary>The value range the column can hold, or null when no practical limit applies.</summary>
    public NumericRange? NumericRange => SqlType.ToLowerInvariant() switch
    {
        "tinyint" => new NumericRange(byte.MinValue, byte.MaxValue),
        "smallint" => new NumericRange(short.MinValue, short.MaxValue),
        "int" => new NumericRange(int.MinValue, int.MaxValue),
        "bigint" => new NumericRange(long.MinValue, long.MaxValue),
        "smallmoney" => new NumericRange(-214_748.3648m, 214_748.3647m),
        "money" => new NumericRange(-922_337_203_685_477.5808m, 922_337_203_685_477.5807m),
        "decimal" or "numeric" => DecimalRange(Precision, Scale),
        _ => null
    };

    /// <summary>The date range the column can hold, or null when it covers the whole DateTime range.</summary>
    public DateTimeRange? DateRange => SqlType.ToLowerInvariant() switch
    {
        "datetime" => new DateTimeRange(new DateTime(1753, 1, 1), new DateTime(9999, 12, 31, 23, 59, 59, 997)),
        "smalldatetime" => new DateTimeRange(new DateTime(1900, 1, 1), new DateTime(2079, 6, 6, 23, 59, 0)),
        _ => null
    };

    private static NumericRange? DecimalRange(byte precision, byte scale)
    {
        var integerDigits = precision - scale;
        if (precision == 0 || integerDigits > 28) return null; // beyond System.Decimal; any parsed value fits

        decimal max = 1m;
        for (var i = 0; i < integerDigits; i++) max *= 10m;
        decimal step = 1m;
        for (var i = 0; i < scale; i++) step /= 10m;
        max -= step;                                   // DECIMAL(6,2) -> 9999.99
        return new NumericRange(-max, max);
    }
}
