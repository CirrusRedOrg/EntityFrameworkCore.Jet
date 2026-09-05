using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using Xunit;

// The byte functions (LenB/AscB/LeftB/RightB/MidB) on a RAW BINARY column — matching ACE, which reinterprets the
// binary as a UTF-16LE string (an odd trailing byte zero-padded) before applying them. Expected values are what
// ACE returned for the same data. Covers odd (3-byte) and even (4-byte) values.
public class ByteFunctionsBinaryTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "bfb-");
        var db = TemporaryDatabase.OpenTracked(path, readOnly: false);
        db.CreateTable("T",
            [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
             new ColumnSpec("B", JetDataType.Binary, 50, IsFixedLength: false)],
            primaryKey: ["K"]);
        db.OpenTable("T").Insert([1, new byte[] { 0x41, 0x42, 0x43 }]);        // 3 bytes (odd)  -> "ABC"
        db.OpenTable("T").Insert([2, new byte[] { 0x41, 0x42, 0x43, 0x44 }]);  // 4 bytes (even) -> "ABCD"
        return new QueryEngine(db);
    }

    private static object? Eval(QueryEngine e, string expr, int k)
        => e.ExecuteQuery($"SELECT {expr} FROM T WHERE K = {k}").Rows.Single()[0];

    [Theory]
    // reinterpreted as UTF-16LE: 3 bytes -> 2 chars (odd byte zero-padded) so LenB = 4, not 3.
    [InlineData("LenB(B)", 1, 4)]
    [InlineData("LenB(B)", 2, 4)]
    [InlineData("AscB(B)", 1, 65)]   // low byte of the first char = 0x41
    [InlineData("AscB(B)", 2, 65)]
    public void Numeric_byte_functions_on_binary(string expr, int k, int expected)
        => Assert.Equal(expected, Convert.ToInt32(Eval(Seeded(), expr, k)));

    // On binary input the slice functions return the raw byte[] (so a further byte function can read the bytes —
    // e.g. ASCB(RightB(x,1))). Values are the actual byte slices ACE operates on (odd input zero-padded to even).
    [Theory]
    [InlineData("LeftB(B, 2)", 1, "4142")]
    [InlineData("RightB(B, 2)", 1, "4300")]   // padded [41,42,43,00] → last 2 bytes
    [InlineData("MidB(B, 2, 2)", 1, "4243")]
    [InlineData("MidB(B, 1, 3)", 1, "414243")]
    [InlineData("LeftB(B, 2)", 2, "4142")]
    [InlineData("RightB(B, 2)", 2, "4344")]
    [InlineData("MidB(B, 2, 2)", 2, "4243")]
    public void Substring_byte_functions_on_binary(string expr, int k, string expectedHex)
        => Assert.Equal(expectedHex, Convert.ToHexString((byte[])Eval(Seeded(), expr, k)!));

    // The EFCore.Jet ByteArrayLength translation runs against LibRed's byte functions (LibRed.EFCore inherits it):
    //   CASE WHEN ASCB(RightB(x,1)) = 0 THEN LenB(x)-1 ELSE LenB(x) END
    // Verified to reproduce ACE's results — including the documented failure (even data ending in 0x00 → -1).
    [Theory]
    [InlineData(1, 3)]   // [41,42,43]      odd  → 3
    [InlineData(2, 4)]   // [41,42,43,44]   even → 4
    public void ByteArrayLength_translation_matches_ace(int k, int expected)
    {
        var e = Seeded();
        int asc = Convert.ToInt32(Eval(e, "ASCB(RightB(B, 1))", k));   // must not crash — reads the last byte
        int lenB = Convert.ToInt32(Eval(e, "LenB(B)", k));
        Assert.Equal(expected, asc == 0 ? lenB - 1 : lenB);
    }
}
