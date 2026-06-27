namespace LibRed.Core.Tests;

/// <summary>Paths to the real database files copied alongside the test assembly.</summary>
internal static class TestDatabases
{
    /// <summary>An Access 2007 (ACE 12 / ACCDB) Northwind sample.</summary>
    public static string NorthwindAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
}
