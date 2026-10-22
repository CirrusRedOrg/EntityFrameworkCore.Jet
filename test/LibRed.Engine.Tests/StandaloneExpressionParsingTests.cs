using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

public class StandaloneExpressionParsingTests
{
    private readonly AntlrSqlParser _parser = new();

    // A stored expression holds no comment: ACE refuses `CHECK (Amount > 0 -- trailing comment)` and the like as a
    // syntax error, in a CHECK and a DEFAULT alike (measured), and reads `--` as two minus signs.
    [Theory]
    [InlineData("Amount > 0; DROP TABLE Products")]
    [InlineData("Amount > 0 unexpected")]
    [InlineData("Amount > 0 -- trailing comment")]
    public void ParseExpression_rejects_trailing_tokens(string sql)
    {
        Assert.Throws<SqlParseException>(() => _parser.ParseExpression(sql));
    }

    [Theory]
    [InlineData("Amount > 0 AND Amount < 100")]
    public void ParseExpression_accepts_a_complete_expression(string sql)
    {
        _parser.ParseExpression(sql);
    }
}
