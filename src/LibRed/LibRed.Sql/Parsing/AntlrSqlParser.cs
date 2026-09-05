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

    public bool IsStatementless(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return true;

        // Ask the lexer, not a text scan: WS/LINE_COMMENT/BLOCK_COMMENT are `-> skip`, so text made only of
        // those produces no tokens at all. Anything else yields at least one, and a `--` inside a string
        // literal stays part of that literal's token rather than starting a comment.
        var lexer = new AccessSqlLexer(new AntlrInputStream(sql));
        lexer.RemoveErrorListeners();   // a malformed statement is ParseStatement's error to report, not ours
        return lexer.NextToken().Type == TokenConstants.EOF;
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

        return AstBuilder.BuildExpression(parser.standaloneExpression().expression());
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
