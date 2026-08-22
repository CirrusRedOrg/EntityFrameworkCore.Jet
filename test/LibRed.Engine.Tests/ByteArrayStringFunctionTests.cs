using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A byte[] value reaching a string function must coerce one char per byte (Jet's binary→string), not via
/// .ToString() (which yields "System.Byte[]"). EF emits this for <c>byte[].Contains(x)</c> as
/// <c>INSTR(1, STRCONV(arr, 64), 0xXX, 0) &gt; 0</c>.
/// </summary>
public class ByteArrayStringFunctionTests : TempDatabaseTest
{
    private static QueryEngine Engine()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "bstr-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE B (Id LONG PRIMARY KEY, Arr VARBINARY(10))");
        e.ExecuteNonQuery("INSERT INTO B (Id, Arr) VALUES (1, 0x414201)"); // A B 0x01
        e.ExecuteNonQuery("INSERT INTO B (Id, Arr) VALUES (2, 0x4142)");   // A B
        return e;
    }

    private static object? Scalar(QueryEngine e, string sql) => e.ExecuteQuery(sql).Rows.First()[0];

    [Fact]
    public void Strconv_64_widens_each_byte_to_a_char()
    {
        var s = (string)Scalar(Engine(), "SELECT STRCONV(Arr, 64) FROM B WHERE Id = 1")!;
        Assert.Equal(new string(new[] { 'A', 'B', (char)1 }), s); // not "System.Byte[]"
    }

    [Fact]
    public void Instr_finds_a_hex_literal_needle_in_a_converted_binary()
    {
        var e = Engine();
        Assert.Equal(3, Convert.ToInt32(Scalar(e, "SELECT INSTR(1, STRCONV(Arr, 64), 0x01, 0) FROM B WHERE Id = 1")));
        Assert.Equal(0, Convert.ToInt32(Scalar(e, "SELECT INSTR(1, STRCONV(Arr, 64), 0x01, 0) FROM B WHERE Id = 2")));
    }

    [Fact]
    public void Byte_array_contains_predicate_matches_only_rows_holding_the_byte()
        // The exact shape EF emits for `ByteArray.Contains((byte)1)`.
        => Assert.Equal(1, Convert.ToInt32(
            Scalar(Engine(), "SELECT COUNT(*) FROM B WHERE INSTR(1, STRCONV(Arr, 64), 0x01, 0) > 0")));
}
