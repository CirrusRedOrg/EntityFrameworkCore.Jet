namespace LibRed.Sql.Ast;

/// <summary>
/// Base type for every node in the SQL abstract syntax tree. The AST is deliberately
/// decoupled from the ANTLR parse tree: the parser lowers the concrete syntax tree
/// into these nodes so the rest of the engine never depends on the grammar.
/// New SQL features are added by introducing new node types here.
/// </summary>
public abstract record SqlNode;
