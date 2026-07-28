using LibRed.Sql.Ast;

namespace LibRed.Sql.Binding;

/// <summary>
/// A statement whose names and types have been resolved against the schema. The
/// query planner consumes this rather than the raw AST so it can assume everything
/// referenced is valid.
/// </summary>
public sealed record BoundStatement(SqlStatement Statement);
