using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

public class StandaloneExpressionParsingTests
{
    private readonly AntlrSqlParser _parser = new();

    [Theory]
    [InlineData("Amount > 0; DROP TABLE Products")]
    [InlineData("Amount > 0 unexpected")]
    public void ParseExpression_rejects_trailing_tokens(string sql)
    {
        Assert.Throws<SqlParseException>(() => _parser.ParseExpression(sql));
    }

    [Theory]
    [InlineData("Amount > 0 AND Amount < 100")]
    [InlineData("Amount > 0 -- trailing comment")]
    public void ParseExpression_accepts_a_complete_expression(string sql)
    {
        _parser.ParseExpression(sql);
    }
}
