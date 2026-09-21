namespace LibRed.Sql.Ast;

/// <summary>Base type for scalar/boolean expressions.</summary>
public abstract record Expression : SqlNode;

/// <summary>A literal constant (number, string, date, boolean or null). <paramref name="Written"/> is the exact value of
/// a number written with a decimal point and no exponent, its scale the digits written after the point (<c>1.50</c> is
/// 1.50); ACE reads such a literal as a Decimal and keeps no more places than that in a product or quotient.</summary>
public sealed record LiteralExpression(object? Value, decimal? Written = null) : Expression;

/// <summary>A reference to a column, optionally table-qualified.</summary>
public sealed record ColumnReference(string? Table, string Column) : Expression;

/// <summary>A positional or named query parameter (e.g. <c>?</c> or <c>@p</c>).</summary>
public sealed record ParameterExpression(string Name) : Expression;

/// <summary>A connection-scoped system variable: <c>@@ROWCOUNT</c> (rows affected by the previous
/// statement) or <c>@@IDENTITY</c> (the last AutoNumber generated on this connection). EF Core emits
/// these to read a store-generated key back after an INSERT. <paramref name="Name"/> is the bare name
/// without the leading <c>@@</c>.</summary>
public sealed record SystemVariableExpression(string Name) : Expression;

/// <summary>The <paramref name="Position"/>th (1-based) column of the rows being sorted — what <c>ORDER BY n</c> names
/// where those rows are already the query's output (a <c>SELECT *</c>, or a set operation). Made by the planner, never
/// parsed.</summary>
public sealed record OutputColumnPosition(int Position) : Expression;

/// <summary><c>*</c> in a projection or aggregate.</summary>
public sealed record StarExpression : Expression;

/// <summary>A table-qualified star, <c>Table.*</c> — all columns of that source. Expanded during
/// projection into the input columns whose source is <paramref name="Table"/>.</summary>
public sealed record QualifiedStarExpression(string Table) : Expression;

public enum BinaryOperator
{
    Add, Subtract, Multiply, Divide, Modulo, IntDivide, Power, Concat,
    Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual,
    And, Or, Xor, Eqv, Imp, Like, In,
    BitAnd, BitOr, BitXor, // Access bitwise operators BAND / BOR / BXOR (integers only)
}

public sealed record BinaryExpression(BinaryOperator Operator, Expression Left, Expression Right) : Expression;

public enum UnaryOperator { Negate, Not, IsNull, IsNotNull, BitNot }

public sealed record UnaryExpression(UnaryOperator Operator, Expression Operand) : Expression;

/// <summary>A scalar/aggregate function call, e.g. <c>Count(*)</c>, <c>IIf(...)</c>, <c>Format(...)</c>.
/// <paramref name="Distinct"/> is set for the ANSI aggregate form <c>COUNT(DISTINCT col)</c> — the aggregate
/// runs over the distinct set of the argument's values (not distinct rows).
/// <paramref name="WithinGroup"/> is set for an ordered-set aggregate, <c>PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY
/// x DESC)</c>: its ORDER BY keys are the last of the <paramref name="Arguments"/>, so that every walker sees them as
/// it sees an argument, and this holds their directions, one per key. <paramref name="Filter"/> is an aggregate's
/// <c>FILTER (WHERE …)</c>: only the rows it is true for go in.</summary>
public sealed record FunctionCall(
    string Name, IReadOnlyList<Expression> Arguments, bool Distinct = false,
    IReadOnlyList<SortDirection>? WithinGroup = null, Expression? Filter = null) : Expression
{
    /// <summary>Whether <paramref name="name"/> is an ordered-set aggregate, which takes WITHIN GROUP and needs it.</summary>
    public static bool IsOrderedSetAggregate(string name) =>
        name.Equals("PERCENTILE_CONT", StringComparison.OrdinalIgnoreCase)
        || name.Equals("PERCENTILE_DISC", StringComparison.OrdinalIgnoreCase)
        || name.Equals("LISTAGG", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="name"/> is a list aggregate — the standard's <c>LISTAGG</c> or SQL
    /// Server's <c>STRING_AGG</c>, which compute the same thing and differ only in what they require: LISTAGG
    /// needs its WITHIN GROUP and lets the separator go, STRING_AGG needs the separator and lets the order
    /// go.</summary>
    public static bool IsListAggregate(string name) =>
        name.Equals("LISTAGG", StringComparison.OrdinalIgnoreCase)
        || name.Equals("STRING_AGG", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="name"/> accepts WITHIN GROUP — the ordered-set aggregates, which
    /// require it, and <c>STRING_AGG</c>, for which it is optional (unordered, the values list in the order
    /// the rows arrive).</summary>
    public static bool AcceptsWithinGroup(string name) =>
        IsOrderedSetAggregate(name) || name.Equals("STRING_AGG", StringComparison.OrdinalIgnoreCase);

    /// <summary>The WITHIN GROUP keys: the last arguments, one per direction.</summary>
    public IReadOnlyList<Expression> WithinGroupKeys =>
        WithinGroup is null ? [] : Arguments.Skip(Arguments.Count - WithinGroup.Count).ToList();
}

/// <summary>The <c>OVER (…)</c> of a window function: how the input is cut into partitions and how rows are
/// ordered within one. An empty <paramref name="PartitionBy"/> means a single partition over the whole input;
/// an empty <paramref name="OrderBy"/> means every row of a partition is a peer. A null <paramref name="Frame"/>
/// is the standard's default frame, <c>RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW</c>
/// (<see cref="WindowFrame.Default"/>).</summary>
public sealed record WindowSpec(
    IReadOnlyList<Expression> PartitionBy,
    IReadOnlyList<OrderByItem> OrderBy,
    WindowFrame? Frame = null) : SqlNode;

/// <summary>How a frame's bounds are counted: in rows, in ORDER BY values, or in peer groups.</summary>
public enum FrameUnit { Rows, Range, Groups }

/// <summary>The kinds of frame bound, in window order — a frame's start never comes after its end in this order.</summary>
public enum FrameBoundKind { UnboundedPreceding, Preceding, CurrentRow, Following, UnboundedFollowing }

/// <summary>One end of a window frame; <paramref name="Offset"/> is set for <c>n PRECEDING</c> and <c>n FOLLOWING</c>.</summary>
public sealed record FrameBound(FrameBoundKind Kind, Expression? Offset = null) : SqlNode;

/// <summary>The rows a frame leaves out around the current row: none, the row itself, its peer group, or its peers
/// but not itself.</summary>
public enum FrameExclusion { NoOthers, CurrentRow, Group, Ties }

/// <summary>A window's frame: the rows of the partition, around the current row, that a frame-reading function —
/// an aggregate, <c>FIRST_VALUE</c>, … — sees.</summary>
public sealed record WindowFrame(FrameUnit Unit, FrameBound Start, FrameBound End, FrameExclusion Exclusion = FrameExclusion.NoOthers)
    : SqlNode
{
    /// <summary><c>RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW</c>: with an ORDER BY, the partition up to the
    /// current row's last peer; without one, the whole partition, every row then being a peer.</summary>
    public static readonly WindowFrame Default =
        new(FrameUnit.Range, new(FrameBoundKind.UnboundedPreceding), new(FrameBoundKind.CurrentRow));
}

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
/// <param name="Name">The function's name, as written.</param>
/// <param name="Arguments">Its arguments, with an ordered-set aggregate's WITHIN GROUP keys last.</param>
/// <param name="Over">The window: its PARTITION BY, ORDER BY and frame.</param>
/// <param name="Distinct">A windowed aggregate over the distinct values of its argument in each frame.</param>
/// <param name="IgnoreNulls">IGNORE NULLS (true) or RESPECT NULLS (false); null when neither is written.</param>
/// <param name="FromLast">FROM LAST (true) or FROM FIRST (false); null when neither is written.</param>
/// <param name="WithinGroup">An ordered-set aggregate's ordering, as on <see cref="FunctionCall"/>.</param>
/// <param name="Filter">An aggregate's FILTER (WHERE …), as on <see cref="FunctionCall"/>.</param>
public sealed record WindowFunction(
    string Name,
    IReadOnlyList<Expression> Arguments,
    WindowSpec Over,
    bool Distinct = false,
    bool? IgnoreNulls = null,
    bool? FromLast = null,
    IReadOnlyList<SortDirection>? WithinGroup = null,
    Expression? Filter = null) : Expression
{
    /// <summary>Every expression the call evaluates on a row: arguments, FILTER, partition and sort keys, and frame
    /// offsets.</summary>
    public IEnumerable<Expression> Expressions() =>
        Arguments
            .Concat(Filter is null ? [] : [Filter])
            .Concat(Over.PartitionBy)
            .Concat(Over.OrderBy.Select(o => o.Value))
            .Concat(new[] { Over.Frame?.Start.Offset, Over.Frame?.End.Offset }.OfType<Expression>());
}

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

/// <summary><c>value [NOT] BETWEEN low AND high</c>: inclusive, with the bounds in either order, and Null when any of
/// the three is Null.</summary>
public sealed record BetweenExpression(Expression Value, Expression Low, Expression High, bool Negated) : Expression;

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
