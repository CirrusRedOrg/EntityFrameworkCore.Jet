namespace LibRed.Catalog;

/// <summary>Join kind in a view (stored as the MSysQueries join-row flag: inner=1, left=2, right=3).</summary>
public enum ViewJoinType { Inner = 1, Left = 2, Right = 3 }

/// <summary>A source in a view's FROM: a named table (<paramref name="Table"/>), or a derived table whose
/// <paramref name="SubquerySql"/> is the verbatim inner subquery (stored in the MSysQueries table row's
/// Expression instead of Name1, with the alias in Name2).</summary>
public sealed record ViewTableSpec(string? Table, string? Alias, string? SubquerySql = null);

/// <summary>A join in a view: kind, verbatim ON condition, and the two tables it joins (from the
/// condition), stored as the join row's Name1/Name2.</summary>
public sealed record ViewJoinSpec(ViewJoinType Kind, string Condition, string LeftAlias, string RightAlias);

/// <summary>An output column of a view: its verbatim expression and optional alias (MSysQueries column
/// row Expression + Name1).</summary>
public sealed record ViewColumnSpec(string Expression, string? Alias);

/// <summary>A declared parameter of a stored (procedure) query: its name and Jet type code, stored as an
/// MSysQueries <c>Attribute=2</c> row (Name1 = name, Flag = <paramref name="TypeCode"/>).</summary>
public sealed record ViewParameterSpec(string Name, byte TypeCode);

/// <summary>An ORDER BY key: verbatim sort expression + direction, stored as an MSysQueries
/// <c>Attribute=0x0B</c> row (Expression = the column, Name1 = "d" when <paramref name="Descending"/>).</summary>
public sealed record ViewOrderBySpec(string Expression, bool Descending);

/// <summary>The kind of stored action query. Access flags the MSysObjects row and the MSysQueries
/// <c>Attribute=0x01</c> row differently for each.</summary>
public enum ActionQueryKind
{
    /// <summary>CREATE TABLE / DROP TABLE etc. — the whole SQL text is stored verbatim.</summary>
    DataDefinition,
    /// <summary>INSERT INTO … — a target table plus the appended columns.</summary>
    Append,
}

/// <summary>One appended column of an INSERT query: the target column and the verbatim value/source
/// expression, stored as an <c>Attribute=0x06</c> row (Name2 = column, Expression = value).</summary>
public sealed record AppendColumnSpec(string Column, string ValueExpression);

/// <summary>A stored action query (a CREATE PROCEDURE body that is not a SELECT). A
/// <see cref="ActionQueryKind.DataDefinition"/> query carries its whole <paramref name="DdlSql"/>; an
/// <see cref="ActionQueryKind.Append"/> query carries a <paramref name="TargetTable"/> and its appended
/// <paramref name="Values"/> (VALUES mode — literal expressions per column).</summary>
public sealed record ActionQuerySpec(
    ActionQueryKind Kind,
    string? DdlSql = null,
    string? TargetTable = null,
    IReadOnlyList<AppendColumnSpec>? Values = null);

/// <summary>
/// A view's decomposed "simple SELECT" — the columns, source tables, joins and WHERE (all verbatim text) —
/// that Access stores as MSysQueries rows. Aggregates / GROUP BY / HAVING / ORDER BY are not permitted.
/// </summary>
public sealed record ViewSpec(
    bool Distinct,
    IReadOnlyList<ViewColumnSpec> Columns,
    IReadOnlyList<ViewTableSpec> Tables,
    IReadOnlyList<ViewJoinSpec> Joins,
    string? Where,
    IReadOnlyList<string>? GroupBy = null,
    IReadOnlyList<ViewParameterSpec>? Parameters = null,
    IReadOnlyList<ViewOrderBySpec>? OrderBy = null,
    int? Top = null);
