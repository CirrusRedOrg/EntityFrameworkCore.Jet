using LibRed.Sql.Ast;

namespace LibRed.Sql.Parsing;

/// <summary>
/// Parses SQL text into an AST. The implementation lowers the ANTLR parse tree into
/// the <see cref="SqlNode"/> hierarchy; callers depend only on this abstraction so the
/// grammar can evolve (or be swapped) freely.
/// </summary>
public interface ISqlParser
{
    /// <summary>Parses a single statement. Throws <see cref="SqlParseException"/> on syntax errors.</summary>
    SqlStatement ParseStatement(string sql);

    /// <summary>
    /// True when <paramref name="sql"/> carries no statement at all — it is empty, whitespace, or nothing but
    /// comments. Such text is not a statement to execute; callers run it as a no-op rather than parsing it,
    /// since the grammar has no production for "nothing" and would report a syntax error at EOF.
    /// </summary>
    /// <remarks>
    /// EF Core produces this from <c>migrationBuilder.Sql("--some note")</c>, which is legitimate and must
    /// succeed silently. The check has to come from the lexer rather than from scanning for <c>--</c>: in
    /// <c>SELECT '--'</c> the dashes are inside a string literal, and a textual strip would wrongly reduce a
    /// real statement to nothing.
    /// </remarks>
    bool IsStatementless(string sql);

    /// <summary>Parses a complete bare scalar expression (e.g. a column DEFAULT value), consuming
    /// the entire input apart from skipped whitespace/comments. Throws <see cref="SqlParseException"/>
    /// on syntax errors or trailing tokens.</summary>
    Expression ParseExpression(string sql);
}
