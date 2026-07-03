using Antlr4.Runtime;
using LibRed.Sql.Ast;
using LibRed.Sql.Grammar;

namespace LibRed.Sql.Parsing;

/// <summary>
/// Default <see cref="ISqlParser"/>: runs the ANTLR-generated lexer/parser for the Access
/// SQL grammar and lowers the parse tree into the AST via <see cref="AstBuilder"/>.
/// </summary>
public sealed class AntlrSqlParser : ISqlParser
{
    public SqlStatement ParseStatement(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var errors = new ThrowingErrorListener();

        var lexer = new AccessSqlLexer(new AntlrInputStream(sql));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errors);

        var parser = new AccessSqlParser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errors);

        return new AstBuilder().Build(parser.statement());
    }

    public Expression ParseExpression(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var errors = new ThrowingErrorListener();

        var lexer = new AccessSqlLexer(new AntlrInputStream(sql));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errors);

        var parser = new AccessSqlParser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errors);

        return AstBuilder.BuildExpression(parser.expression());
    }

    private sealed class ThrowingErrorListener : IAntlrErrorListener<int>, IAntlrErrorListener<IToken>
    {
        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e) =>
            throw new SqlParseException(msg, line, charPositionInLine);

        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e) =>
            throw new SqlParseException(msg, line, charPositionInLine);
    }
}
