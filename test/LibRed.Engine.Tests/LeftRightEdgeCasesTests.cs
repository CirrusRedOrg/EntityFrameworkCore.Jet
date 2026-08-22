using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Left/Right edge cases, verified byte-identical to ACE — including where ACE errors (negative length → Invalid
// procedure call; null length → Data type mismatch) rather than clamping. A null string propagates. Also: Split()
// is not a scalar SQL function in ACE ("Undefined function", it returns an array), so LibRed rejects it too.
public class LeftRightEdgeCasesTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "lr-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return e;
    }

    private static object? Eval(string expr) => Fresh().ExecuteQuery($"SELECT {expr} FROM T").Rows.Single()[0];

    [Theory]
    [InlineData("Left('abcdef', 3)", "abc")]
    [InlineData("Left('abcdef', 0)", "")]
    [InlineData("Left('abcdef', 10)", "abcdef")]     // over-length → whole string
    [InlineData("Right('abcdef', 3)", "def")]
    [InlineData("Right('abcdef', 0)", "")]
    [InlineData("Right('abcdef', 10)", "abcdef")]
    public void Matches_ace(string expr, string expected)
        => Assert.Equal(expected, Convert.ToString(Eval(expr)));

    [Theory]
    [InlineData("Left(Null, 2)")]
    [InlineData("Right(Null, 2)")]
    public void Null_string_propagates(string expr)
        => Assert.Null(Eval(expr));

    [Theory]
    [InlineData("Left('abcdef', -1)")]      // negative → Invalid procedure call
    [InlineData("Right('abcdef', -1)")]
    [InlineData("Left('abc', Null)")]       // null length → Data type mismatch
    [InlineData("Right('abc', Null)")]
    public void Error_cases(string expr)
        => Assert.Throws<InvalidOperationException>(() => Eval(expr));

    [Fact]
    public void Split_is_not_a_scalar_function() // matches ACE ("Undefined function 'Split'")
        => Assert.Throws<NotSupportedException>(() => Eval("Split('a,b,c', ',')"));
}
