namespace LibRed.Sql.Ast;

/// <summary>Base type for scalar/boolean expressions.</summary>
public abstract record Expression : SqlNode;

/// <summary>A literal constant (number, string, date, boolean or null).</summary>
public sealed record LiteralExpression(object? Value) : Expression;

/// <summary>A reference to a column, optionally table-qualified.</summary>
public sealed record ColumnReference(string? Table, string Column) : Expression;

/// <summary>A positional or named query parameter (e.g. <c>?</c> or <c>@p</c>).</summary>
public sealed record ParameterExpression(string Name) : Expression;

/// <summary>A connection-scoped system variable: <c>@@ROWCOUNT</c> (rows affected by the previous
/// statement) or <c>@@IDENTITY</c> (the last AutoNumber generated on this connection). EF Core emits
/// these to read a store-generated key back after an INSERT. <paramref name="Name"/> is the bare name
/// without the leading <c>@@</c>.</summary>
public sealed record SystemVariableExpression(string Name) : Expression;

/// <summary><c>*</c> in a projection or aggregate.</summary>
public sealed record StarExpression : Expression;

/// <summary>A table-qualified star, <c>Table.*</c> — all columns of that source. Expanded during
/// projection into the input columns whose source is <paramref name="Table"/>.</summary>
public sealed record QualifiedStarExpression(string Table) : Expression;

public enum BinaryOperator
{
    Add, Subtract, Multiply, Divide, Modulo, IntDivide, Power, Concat,
    Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual,
    And, Or, Like, In,
    BitAnd, BitOr, BitXor, // Access bitwise operators BAND / BOR / BXOR (integers only)
}

public sealed record BinaryExpression(BinaryOperator Operator, Expression Left, Expression Right) : Expression;

public enum UnaryOperator { Negate, Not, IsNull, IsNotNull, BitNot }

public sealed record UnaryExpression(UnaryOperator Operator, Expression Operand) : Expression;

/// <summary>A scalar/aggregate function call, e.g. <c>Count(*)</c>, <c>IIf(...)</c>, <c>Format(...)</c>.
/// <paramref name="Distinct"/> is set for the ANSI aggregate form <c>COUNT(DISTINCT col)</c> — the aggregate
/// runs over the distinct set of the argument's values (not distinct rows).</summary>
public sealed record FunctionCall(string Name, IReadOnlyList<Expression> Arguments, bool Distinct = false) : Expression;

/// <summary>The <c>OVER (…)</c> of a window function: how the input is cut into partitions and how rows are
/// ordered within one. An empty <paramref name="PartitionBy"/> means a single partition over the whole input;
/// an empty <paramref name="OrderBy"/> means every row of a partition is a peer. A frame clause belongs here
/// when one is needed — adding it is a new optional property on this record and nothing else.</summary>
public sealed record WindowSpec(
    IReadOnlyList<Expression> PartitionBy,
    IReadOnlyList<OrderByItem> OrderBy) : SqlNode;

/// <summary>
/// A window function call: <c>ROW_NUMBER() OVER (PARTITION BY … ORDER BY …)</c>. Access has none of these —
/// this is a LibRed extension, emitted by EF Core's base SQL generator in extended mode.
/// </summary>
/// <remarks>
/// Deliberately NOT a subtype of <see cref="FunctionCall"/>, and that is load-bearing rather than tidiness:
/// <c>QueryPlanner.HasAggregate</c> matches any <see cref="FunctionCall"/> whose name is an aggregate, so a
/// windowed aggregate (<c>SUM(x) OVER (…)</c>) would make the query look grouped and build a bogus
/// AggregateNode. As a sibling record it falls through to "not an aggregate", which is correct — a window
/// function returns one value per ROW, not per group, whatever its name.
/// </remarks>
public sealed record WindowFunction(
    string Name,
    IReadOnlyList<Expression> Arguments,
    WindowSpec Over) : Expression;

/// <summary>A subquery used as a scalar value: <c>(SELECT … )</c>. May correlate to the outer query.</summary>
/// <remarks>
/// The query is any <see cref="SqlStatement"/> query — a <see cref="SelectStatement"/>, a
/// <see cref="SetOperationStatement"/> (a UNION and friends) or a <see cref="ValuesStatement"/> — because the
/// standard reaches a subquery through the same <c>&lt;query expression&gt;</c> nonterminal as a derived table,
/// where <see cref="SubqueryTable"/> has always been typed this way. Consumers that inspect a subquery's shape
/// (the decorrelation rewrites) must therefore decline anything that is not a plain SELECT rather than assume.
/// </remarks>
public sealed record ScalarSubquery(SqlStatement Query) : Expression;

/// <summary><c>EXISTS (SELECT … )</c>: true when the (possibly correlated) subquery returns any row.
/// The query is any query statement — see <see cref="ScalarSubquery"/>.</summary>
public sealed record ExistsExpression(SqlStatement Query) : Expression;

/// <summary><c>x [NOT] IN (SELECT … )</c>: membership of <paramref name="Value"/> in the first column of a
/// (possibly correlated) subquery, with SQL three-valued semantics. The query is any query statement — see
/// <see cref="ScalarSubquery"/>.</summary>
public sealed record InSubqueryExpression(Expression Value, SqlStatement Query, bool Negated) : Expression;

/// <summary><c>x [NOT] IN (a, b, …)</c> over a literal value list, kept as a flat node (rather than lowered to a
/// deep <c>OR</c>-chain) so a very large list — EF Core inlines a "huge number of values" Contains as thousands
/// of constants — evaluates iteratively instead of recursing once per item and overflowing the stack. Same SQL
/// three-valued semantics as <see cref="InSubqueryExpression"/>.</summary>
public sealed record InListExpression(Expression Value, IReadOnlyList<Expression> Items, bool Negated) : Expression;

/// <summary>
/// The <c>DEFAULT</c> keyword used as a row value in an INSERT's table value constructor:
/// <c>VALUES ('Advertisement', DEFAULT)</c>. It is a marker rather than a value — the column takes its
/// declared default, or NULL when it has none — so it never reaches the expression evaluator, and the
/// grammar admits it only inside an INSERT, which is the one place the standard allows it.
/// </summary>
public sealed record DefaultValueExpression : Expression;

/// <summary>One <c>WHEN condition THEN result</c> arm of a <see cref="CaseExpression"/>.</summary>
public sealed record CaseWhen(Expression Condition, Expression Result) : SqlNode;

/// <summary>
/// Standard SQL <c>CASE</c>. Access/ACE has no CASE at all — only the <c>IIF()</c> function — so this is
/// reachable from LibRed's extended SQL mode and from hand-written SQL, never from the Jet-compatible
/// generator, which rewrites a CASE into nested IIFs instead.
/// </summary>
/// <remarks>
/// The simple form <c>CASE operand WHEN value THEN …</c> is folded into the searched form at parse time by
/// rewriting each arm's condition to <c>operand = value</c>, so only one shape reaches evaluation. Arms are
/// tested in order and the first true one wins; an unmatched CASE with no <paramref name="ElseResult"/>
/// yields NULL, per the standard.
/// </remarks>
public sealed record CaseExpression(IReadOnlyList<CaseWhen> WhenClauses, Expression? ElseResult) : Expression;
