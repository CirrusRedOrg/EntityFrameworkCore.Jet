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
}
