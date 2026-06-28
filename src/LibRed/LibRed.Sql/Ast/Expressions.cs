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

public enum BinaryOperator
{
    Add, Subtract, Multiply, Divide, Modulo, IntDivide, Concat,
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
