namespace LibRed.Sql.Ast;

/// <summary>Base type for scalar/boolean expressions.</summary>
public abstract record Expression : SqlNode;

/// <summary>A literal constant (number, string, date, boolean or null).</summary>
public sealed record LiteralExpression(object? Value) : Expression;

/// <summary>A reference to a column, optionally table-qualified.</summary>
public sealed record ColumnReference(string? Table, string Column) : Expression;

/// <summary>A positional or named query parameter (e.g. <c>?</c> or <c>@p</c>).</summary>
public sealed record ParameterExpression(string Name) : Expression;

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
}

public sealed record BinaryExpression(BinaryOperator Operator, Expression Left, Expression Right) : Expression;

public enum UnaryOperator { Negate, Not, IsNull, IsNotNull }

public sealed record UnaryExpression(UnaryOperator Operator, Expression Operand) : Expression;

/// <summary>A scalar/aggregate function call, e.g. <c>Count(*)</c>, <c>IIf(...)</c>, <c>Format(...)</c>.</summary>
public sealed record FunctionCall(string Name, IReadOnlyList<Expression> Arguments) : Expression;

/// <summary>A subquery used as a scalar value: <c>(SELECT … )</c>. May correlate to the outer query.</summary>
public sealed record ScalarSubquery(SelectStatement Query) : Expression;

/// <summary><c>EXISTS (SELECT … )</c>: true when the (possibly correlated) subquery returns any row.</summary>
public sealed record ExistsExpression(SelectStatement Query) : Expression;

/// <summary><c>x [NOT] IN (SELECT … )</c>: membership of <paramref name="Value"/> in the first column of a
/// (possibly correlated) subquery, with SQL three-valued semantics.</summary>
public sealed record InSubqueryExpression(Expression Value, SelectStatement Query, bool Negated) : Expression;
