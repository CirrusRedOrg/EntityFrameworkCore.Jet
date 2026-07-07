using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using Xunit;

// The byte functions (LenB/AscB/LeftB/RightB/MidB) on a RAW BINARY column — matching ACE, which reinterprets the
// binary as a UTF-16LE string (an odd trailing byte zero-padded) before applying them. Expected values are what
// ACE returned for the same data. Covers odd (3-byte) and even (4-byte) values.
public class ByteFunctionsBinaryTests
{
    private static QueryEngine Seeded()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bfb-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var db = JetDatabase.Open(path, readOnly: false);
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

    [Theory]
    [InlineData("LeftB(B, 2)", 1, "䉁")]   // bytes 41,42 as UTF-16LE = U+4241
    [InlineData("RightB(B, 2)", 1, "C")]  // trailing byte 43 zero-padded = U+0043 = 'C'
    [InlineData("MidB(B, 2, 2)", 1, "䍂")] // bytes 42,43 = U+4342
    [InlineData("MidB(B, 1, 3)", 1, "䉁")] // 3 bytes -> 1 char
    [InlineData("LeftB(B, 2)", 2, "䉁")]
    [InlineData("RightB(B, 2)", 2, "䑃")]  // bytes 43,44 = U+4443
    [InlineData("MidB(B, 2, 2)", 2, "䍂")]
    public void Substring_byte_functions_on_binary(string expr, int k, string expected)
        => Assert.Equal(expected, Convert.ToString(Eval(Seeded(), expr, k)));
}
