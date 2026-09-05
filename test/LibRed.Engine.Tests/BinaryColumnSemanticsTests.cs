using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// A binary column has TWO faces in Access SQL, and both are verified against ACE:
//
//   =, <, >, ORDER BY          -> BYTES. Case-sensitive, byte order.
//   LIKE, Len, &, TypeName     -> a UTF-16 STRING. LIKE brings the text collation with it, so it is
//                                 CASE-INSENSITIVE over the very same column that '=' compares byte-wise.
//
// So `B = 0x4100` matches only 'A', while `B LIKE 'A%'` matches 'A' (0x4100) AND 'a' (0x6100). That is not a
// distinction anyone would implement by accident, which is why it is pinned here.
//
// LibRed previously got the byte half right and the text half badly wrong: it called ToString() on the
// byte[], so Len returned 13 ("System.Byte[]".Length) for every value, `&` produced "System.Byte[]x", and
// LIKE matched nothing. Silent nonsense rather than an error.
public class BinaryColumnSemanticsTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "binsem-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE `BS` (`Id` LONG NOT NULL PRIMARY KEY, `B` VARBINARY(16))");
        engine.ExecuteNonQuery("INSERT INTO `BS` (`Id`, `B`) VALUES (1, 0x4100)");       // 'A'
        engine.ExecuteNonQuery("INSERT INTO `BS` (`Id`, `B`) VALUES (2, 0x6100)");       // 'a'
        engine.ExecuteNonQuery("INSERT INTO `BS` (`Id`, `B`) VALUES (3, 0x4200)");       // 'B'
        engine.ExecuteNonQuery("INSERT INTO `BS` (`Id`, `B`) VALUES (4, 0x41004200)");   // 'AB'
        engine.ExecuteNonQuery("INSERT INTO `BS` (`Id`, `B`) VALUES (5, 0x0102)");       // U+0201, not ASCII
        engine.ExecuteNonQuery("INSERT INTO `BS` (`Id`, `B`) VALUES (6, '')");           // empty
        return engine;
    }

    private static int Count(QueryEngine engine, string where) =>
        Convert.ToInt32(engine.ExecuteQuery($"SELECT COUNT(*) FROM `BS` WHERE {where}").Rows.Single()[0]);

    private static object? Scalar(QueryEngine engine, string projection, int id) =>
        engine.ExecuteQuery($"SELECT {projection} FROM `BS` WHERE `Id` = {id}").Rows.Single()[0];

    // Len counts CHARACTERS, LenB counts BYTES — the clearest statement that the value is a UTF-16 string.
    [Theory]
    [InlineData(1, 1, 2)]   // 0x4100     -> "A"
    [InlineData(4, 2, 4)]   // 0x41004200 -> "AB"
    [InlineData(5, 1, 2)]   // 0x0102     -> one character (U+0201)
    [InlineData(6, 0, 0)]   // empty
    public void Len_counts_characters_and_LenB_counts_bytes(int id, int expectedLen, int expectedLenB)
    {
        QueryEngine engine = Seeded();
        Assert.Equal(expectedLen, Convert.ToInt32(Scalar(engine, "Len(`B`)", id)));
        Assert.Equal(expectedLenB, Convert.ToInt32(Scalar(engine, "LenB(`B`)", id)));
    }

    // Equality is byte-wise: 'A' and 'a' are different values despite Access's case-insensitive text default.
    [Fact]
    public void Equality_is_byte_wise_and_case_sensitive()
    {
        QueryEngine engine = Seeded();
        Assert.Equal(1, Count(engine, "`B` = 0x4100"));
        Assert.Equal(1, Count(engine, "`B` = 0x6100"));
    }

    // Ordering is byte order, not text order: 0x4200 ('B') sorts BEFORE 0x6100 ('a'), which a
    // case-insensitive text sort would reverse.
    [Fact]
    public void Ordering_is_byte_order_not_text_order()
    {
        QueryEngine engine = Seeded();
        var ids = engine.ExecuteQuery("SELECT `Id` FROM `BS` WHERE `Id` IN (2,3) ORDER BY `B`")
            .Rows.Select(r => Convert.ToInt32(r[0])).ToList();

        Assert.Equal([3, 2], ids);
    }

    [Fact]
    public void Relational_comparison_is_byte_wise()
    {
        QueryEngine engine = Seeded();
        Assert.Equal(1, Count(engine, "`B` > 0x4200"));   // only 0x6100
    }

    // The one that surprises: LIKE reads the value as text, so it is case-INsensitive on the same column
    // that '=' compares case-sensitively.
    [Fact]
    public void Like_is_case_insensitive_text_matching()
    {
        QueryEngine engine = Seeded();
        Assert.Equal(3, Count(engine, "`B` LIKE 'A%'"));   // 'A', 'a', 'AB'
        Assert.Equal(3, Count(engine, "`B` LIKE 'a%'"));   // the same three
        Assert.Equal(1, Count(engine, "`B` LIKE 'A_'"));   // 'AB' — '_' is one character
    }

    // Concatenation reinterprets the bytes as UTF-16, including bytes that are not ASCII at all.
    [Fact]
    public void Concatenation_reads_the_bytes_as_utf16()
    {
        QueryEngine engine = Seeded();
        Assert.Equal("Ax", Scalar(engine, "`B` & 'x'", 1));
        Assert.Equal("ȁx", Scalar(engine, "`B` & 'x'", 5));   // 0x0102 little-endian is U+0201
    }

    // TypeName reports String, not Byte[] — the expression service sees a VT_BSTR (VarType 8).
    [Fact]
    public void TypeName_and_VarType_report_a_string()
    {
        QueryEngine engine = Seeded();
        Assert.Equal("String", Scalar(engine, "TypeName(`B`)", 1));
        Assert.Equal("String", Scalar(engine, "TypeName(`B`)", 5));
        Assert.Equal(8, Convert.ToInt32(Scalar(engine, "VarType(`B`)", 1)));
    }
}
