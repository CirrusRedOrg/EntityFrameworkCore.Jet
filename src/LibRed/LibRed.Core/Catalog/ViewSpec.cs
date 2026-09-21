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

/// <summary>
/// A declared parameter of a stored (procedure) query: its name and Jet type code, stored as an MSysQueries
/// <c>Attribute=2</c> row (Name1 = name, Flag = <paramref name="TypeCode"/>). The row's <c>LvExtra</c> carries
/// the declared facets, and Access renders the query's PARAMETERS clause from it — see
/// <see cref="StoredQueryFormat.PackParameterFacets"/> for which types have one and how a decimal's precision
/// and scale pack into the single value.
/// </summary>
public sealed record ViewParameterSpec(string Name, byte TypeCode, int? Size = null, int? Scale = null);

/// <summary>An ORDER BY key: verbatim sort expression + direction, stored as an MSysQueries
/// <c>Attribute=0x0B</c> row (Expression = the column, Name1 = "d" when <paramref name="Descending"/>).</summary>
public sealed record ViewOrderBySpec(string Expression, bool Descending);

/// <summary>The kind of stored action query. Access flags the MSysObjects row and the MSysQueries
/// <c>Attribute=0x01</c> row differently for each.</summary>
public enum ActionQueryKind
{
    /// <summary>CREATE TABLE / DROP TABLE etc. — the whole SQL text is stored verbatim.</summary>
    DataDefinition,
    /// <summary>INSERT INTO … — a target table plus the appended columns, from VALUES or from a SELECT.</summary>
    Append,
    /// <summary>UPDATE … SET — the assignments in <see cref="ActionQuerySpec.Values"/>, over the
    /// <see cref="ActionQuerySpec.Body"/>'s sources.</summary>
    Update,
    /// <summary>DELETE — the <see cref="ActionQuerySpec.Body"/>'s sources and WHERE, and optionally the
    /// <c>table.*</c> target Access stores when the query names one.</summary>
    Delete,
    /// <summary>SELECT … INTO — a SELECT whose target table is on the action row.</summary>
    MakeTable,
}

/// <summary>One column/value pair of an action query, stored as an <c>Attribute=0x06</c> row (Name2 = the
/// column, Expression = the value): an appended column of an INSERT — whose value is a literal for the VALUES
/// form and a source expression for the SELECT form — or one assignment of an UPDATE, whose
/// <paramref name="Column"/> is table-qualified when the update runs over a join.</summary>
public sealed record AppendColumnSpec(string Column, string ValueExpression);

/// <summary>
/// A stored action query (a CREATE PROCEDURE body that is not a plain SELECT). A
/// <see cref="ActionQueryKind.DataDefinition"/> query carries its whole <paramref name="DdlSql"/> and nothing
/// else — Access does not decompose it. Every other kind stores its sources, joins and WHERE exactly as a view
/// does, in <paramref name="Body"/>, and differs only in the action row and in what its column rows mean:
/// <paramref name="Values"/> holds an append's columns or an update's assignments, <paramref name="TargetTable"/>
/// the table an append or make-table writes into, and the body's own columns are a make-table's output list.
/// <paramref name="Parameters"/> are declared exactly as a parameterized SELECT declares them.
/// </summary>
public sealed record ActionQuerySpec(
    ActionQueryKind Kind,
    string? DdlSql = null,
    string? TargetTable = null,
    IReadOnlyList<AppendColumnSpec>? Values = null,
    ViewSpec? Body = null,
    IReadOnlyList<ViewParameterSpec>? Parameters = null,
    string? DeleteTarget = null);

/// <summary>A stored action query read back from the catalog. <paramref name="Sql"/> is the reconstructed,
/// executable statement when LibRed supports the kind; otherwise it is null and <paramref name="UnsupportedReason"/>
/// explains why executing it throws.</summary>
public sealed record StoredActionQuery(string? Sql, string? UnsupportedReason);

/// <summary>A parameter a stored query declares, in declaration order. <paramref name="Type"/> is the Jet type
/// Access recorded for it, or null for its untyped parameter — the one Access renders as the keyword
/// <c>Value</c>. <paramref name="Size"/> is a text parameter's declared length and
/// <paramref name="Precision"/>/<paramref name="Scale"/> a decimal's, all read back from the row's
/// <c>LvExtra</c>; they are null for a type that declares none.</summary>
public sealed record StoredQueryParameter(
    string Name, JetDataType? Type, int? Size = null, int? Precision = null, int? Scale = null);

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