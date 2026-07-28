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

    /// <summary>Parses a complete bare scalar expression (e.g. a column DEFAULT value), consuming
    /// the entire input apart from skipped whitespace/comments. Throws <see cref="SqlParseException"/>
    /// on syntax errors or trailing tokens.</summary>
    Expression ParseExpression(string sql);
}
