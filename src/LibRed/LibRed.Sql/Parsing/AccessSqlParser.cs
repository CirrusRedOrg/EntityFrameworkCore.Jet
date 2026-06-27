using LibRed.Sql.Ast;

namespace LibRed.Sql.Parsing;

/// <summary>
/// Default <see cref="ISqlParser"/>. Will drive the ANTLR-generated lexer/parser and
/// translate its visitor output into the AST. Currently a stub.
/// </summary>
public sealed class AccessSqlParser : ISqlParser
{
    public SqlStatement ParseStatement(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        // TODO: run the ANTLR lexer + parser, then lower the parse tree via an
        // AstBuildingVisitor into the SqlNode hierarchy.
        throw new NotImplementedException("SQL parsing is not yet implemented.");
    }
}
