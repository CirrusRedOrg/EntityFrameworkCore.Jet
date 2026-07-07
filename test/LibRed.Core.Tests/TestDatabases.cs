namespace LibRed.Core.Tests;

/// <summary>Paths to the real database files copied alongside the test assembly.</summary>
internal static class TestDatabases
{
    /// <summary>An Access 2007 (ACE 12 / ACCDB) Northwind sample.</summary>
    public static string NorthwindAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    /// <summary>A 200-column ACCDB whose table definition spans multiple TDEF pages.</summary>
    public static string WideTableAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "WideTable.accdb");

    /// <summary>An ACCDB with Decimal/Numeric columns and known values.</summary>
    public static string DecimalsAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "Decimals.accdb");

    /// <summary>An ACE 16 (version 0x06) ACCDB using the Office 2016 BIGINT and DATETIME2 types.</summary>
    public static string Ace16TypesAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "Ace16Types.accdb");

    /// <summary>EF Core's BuiltInDataTypes database — broad coverage of every mapped column type.</summary>
    public static string BuiltInDataTypesAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "BuiltInDataTypes.accdb");

    /// <summary>EF Core's EverythingIsBytes database — every entity has a byte[] primary key (3/4/5/8/16-byte
    /// values), for byte-faithful binary index-key checks.</summary>
    public static string EverythingIsBytesAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "EverythingIsBytes.accdb");
}
