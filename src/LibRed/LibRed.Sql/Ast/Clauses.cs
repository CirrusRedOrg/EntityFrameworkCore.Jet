namespace LibRed.Sql.Ast;

/// <summary>Base type for a source of rows in a FROM clause.</summary>
public abstract record TableReference : SqlNode;

/// <summary>A named base table or stored query, optionally aliased. <paramref name="Database"/> is the file it is in
/// when its FROM names one with <c>IN externaldatabase</c>, and null for the database the statement runs on.</summary>
public sealed record NamedTable(string Name, string? Alias, ExternalDatabase? Database = null) : TableReference;

/// <summary>
/// An <c>IN externaldatabase</c> clause, as written: the quoted <paramref name="Path"/> (null when it is the empty
/// string, which a connect string then follows) and the bracketed or quoted <paramref name="Connect"/> after it —
/// a database type such as <c>MS Access;</c>, or a whole connect string such as <c>;DATABASE=C:\db.accdb</c>.
/// These are the two values ACE stores for it (Name1 and Expression of the query's <c>0x04</c> row, verified).
/// </summary>
public sealed record ExternalDatabase(string? Path, string? Connect)
{
    /// <summary>The database type: the connect string's first part, empty for an Access database, which is what
    /// ACE assumes when none is named.</summary>
    public string Type => Connect is null ? "" : Connect.Split(';')[0].Trim();

    /// <summary>The file named: the path, or the connect string's <c>DATABASE</c> setting. Null when neither
    /// names one.</summary>
    public string? File => Path ?? Setting("DATABASE");

    /// <summary>The connect string's <c>PWD</c> setting — an encrypted database's password — or null.</summary>
    public string? Password => Setting("PWD");

    private string? Setting(string key) => Connect?.Split(';').Skip(1)
        .Select(part => part.Split('=', 2))
        .Where(pair => pair.Length == 2 && string.Equals(pair[0].Trim(), key, StringComparison.OrdinalIgnoreCase))
        .Select(pair => pair[1].Trim())
        .FirstOrDefault();
}

/// <summary>A derived table (subquery) in the FROM clause. The query is any <see cref="SqlStatement"/>
/// query — a <see cref="SelectStatement"/> or a <see cref="SetOperationStatement"/> (e.g. a UNION).
/// <paramref name="Columns"/> is its column list, <c>AS t(a, b)</c>, naming its columns in order; null without
/// one.</summary>
public sealed record SubqueryTable(SqlStatement Query, string? Alias, IReadOnlyList<string>? Columns = null) : TableReference;

/// <summary>
/// <see cref="Full"/> is a LibRed extension - ACE has no full outer join. So are <see cref="CrossApply"/> and
/// <see cref="OuterApply"/>, the two <b>lateral</b> kinds: their right side is evaluated once per left row with
/// that row in scope (so it may correlate to the left) and they carry no ON condition. CROSS APPLY drops a left
/// row whose right side produced nothing; OUTER APPLY keeps it null-padded, as <see cref="Left"/> does.
/// </summary>
public enum JoinKind { Inner, Left, Right, Cross, Full, CrossApply, OuterApply }

/// <summary>A join between two table references with an ON condition (null for the kinds that take none:
/// <see cref="JoinKind.Cross"/> and the two APPLY kinds).</summary>
public sealed record JoinTable(
    TableReference Left,
    TableReference Right,
    JoinKind Kind,
    Expression? On) : TableReference;

public enum SortDirection { Ascending, Descending }

public sealed record OrderByItem(Expression Value, SortDirection Direction) : SqlNode;
