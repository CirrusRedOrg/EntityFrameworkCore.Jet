namespace LibRed.Core.Tests;

/// <summary>Paths to the real database files copied alongside the test assembly.</summary>
internal static class TestDatabases
{
    /// <summary>The path to a checked-in fixture by file name — every <c>Data\*.accdb</c> is copied
    /// alongside the test assembly. Use this for the sort-order fixtures, one per Access "New database sort
    /// order" entry, rather than adding a property each.</summary>
    public static string Data(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Data", fileName);

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

    /// <summary>An Access-authored ACCDB using the Spanish Traditional sort order, where "ch" and "ll" are
    /// single letters sorting after "c" and "l".</summary>
    public static string SpanishTraditionalAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "SpanishTraditional.accdb");

    /// <summary>An Access-authored ACCDB using the Spanish Modern sort order, which sorts "ch" and "ll" as
    /// the plain letter pairs. Differs from <see cref="SpanishTraditionalAccdb"/> in that alone.</summary>
    public static string SpanishModernAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "SpanishModern.accdb");

    /// <summary>A password-encrypted ACCDB (Office Agile encryption; the password is "Test").</summary>
    public static string EncryptedAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "EncryptedTest.accdb");

    /// <summary>The password for <see cref="EncryptedAccdb"/>.</summary>
    public const string EncryptedPassword = "Test";
}
