using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// An empty byte[] has no hex literal in Access SQL: a bare '0x' is the T-SQL form and ACE rejects it
// ("Syntax error in query expression '0x'"). The literal Access accepts is an empty STRING, which it stores
// as a zero-length binary, distinct from NULL.
//
// JetByteArrayTypeMapping.GenerateNonNullSqlLiteral now emits '' for an empty array, so LibRed has to decode
// it the same way ACE does or the generator fix just moves the divergence. Verified against ACE: '' reads
// back as byte[0]; NULL reads back as NULL; 0x00 is a ONE-byte zero, not empty.
public class EmptyBinaryLiteralTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "emptybin-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE `EB` (`Id` LONG NOT NULL PRIMARY KEY, `B` VARBINARY(8))");
        return engine;
    }

    private static object? ValueOf(QueryEngine engine, int id) =>
        engine.ExecuteQuery($"SELECT `B` FROM `EB` WHERE `Id` = {id}").Rows.Single()[0];

    [Fact]
    public void An_empty_string_literal_stores_a_zero_length_binary()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("INSERT INTO `EB` (`Id`, `B`) VALUES (1, '')");

        object? value = ValueOf(engine, 1);
        byte[] bytes = Assert.IsType<byte[]>(value);
        Assert.Empty(bytes);
    }

    // Empty and NULL must stay distinguishable — collapsing '' to NULL would silently lose the difference
    // that ACE preserves.
    [Fact]
    public void An_empty_binary_is_not_null()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("INSERT INTO `EB` (`Id`, `B`) VALUES (1, '')");
        engine.ExecuteNonQuery("INSERT INTO `EB` (`Id`, `B`) VALUES (2, NULL)");

        Assert.NotNull(ValueOf(engine, 1));
        Assert.Null(ValueOf(engine, 2));
        Assert.Equal(1, Convert.ToInt32(
            engine.ExecuteQuery("SELECT COUNT(*) FROM `EB` WHERE `B` IS NULL").Rows.Single()[0]));
    }

    // 0x00 is a one-byte zero, not an empty array. Getting these confused is exactly the mistake the bare
    // '0x' literal invited.
    [Fact]
    public void A_single_zero_byte_is_not_an_empty_binary()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("INSERT INTO `EB` (`Id`, `B`) VALUES (1, '')");
        engine.ExecuteNonQuery("INSERT INTO `EB` (`Id`, `B`) VALUES (2, 0x00)");

        Assert.Empty(Assert.IsType<byte[]>(ValueOf(engine, 1)));
        Assert.Equal([0], Assert.IsType<byte[]>(ValueOf(engine, 2)));
    }

    // The general rule the empty case falls out of: a string in a binary column stores its UTF-16LE bytes,
    // and is NOT parsed as hex. Verified against ACE for VARBINARY and LONGBINARY alike.
    [Theory]
    [InlineData("A", new byte[] { 0x41, 0x00 })]
    [InlineData("AB", new byte[] { 0x41, 0x00, 0x42, 0x00 })]
    [InlineData("41", new byte[] { 0x34, 0x00, 0x31, 0x00 })]   // the digits '4','1' — not the byte 0x41
    [InlineData("é", new byte[] { 0xE9, 0x00 })]
    public void A_string_in_a_binary_column_stores_its_utf16_bytes(string text, byte[] expected)
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery($"INSERT INTO `EB` (`Id`, `B`) VALUES (1, '{text}')");

        Assert.Equal(expected, Assert.IsType<byte[]>(ValueOf(engine, 1)));
    }

    // The non-empty path is unchanged and still round-trips.
    [Fact]
    public void A_hex_literal_still_round_trips()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("INSERT INTO `EB` (`Id`, `B`) VALUES (1, 0x0102)");

        Assert.Equal([0x01, 0x02], Assert.IsType<byte[]>(ValueOf(engine, 1)));
    }

    // A digitless 0x is rejected, as ACE rejects it. Tolerating it would hide the generator bug this fix
    // exists to correct.
    [Fact]
    public void A_digitless_hex_literal_is_rejected()
    {
        QueryEngine engine = Fresh();
        Assert.ThrowsAny<Exception>(() =>
            engine.ExecuteNonQuery("INSERT INTO `EB` (`Id`, `B`) VALUES (1, 0x)"));
    }
}
