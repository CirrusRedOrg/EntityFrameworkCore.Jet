namespace LibRed.Engine.Execution;

/// <summary>
/// The shape and rows produced by executing a query plan. Rows are arrays of boxed
/// values aligned to <see cref="ColumnNames"/>; the ADO layer projects these into a
/// <c>DbDataReader</c>.
/// </summary>
public sealed class ResultSet
{
    private readonly Func<IReadOnlyList<ResultColumn>>? _describe;
    private IReadOnlyList<ResultColumn>? _columns;

    public ResultSet(
        IReadOnlyList<string> columnNames,
        IEnumerable<object?[]> rows,
        IReadOnlyList<Type>? columnTypes = null,
        Func<IReadOnlyList<ResultColumn>>? describe = null)
    {
        if (columnTypes is not null && columnTypes.Count != columnNames.Count)
            throw new ArgumentException("The number of column types must match the number of column names.", nameof(columnTypes));

        ColumnNames = columnNames;
        Rows = rows;
        ColumnTypes = columnTypes ?? Enumerable.Repeat(typeof(object), columnNames.Count).ToArray();
        _describe = describe;
    }

    /// <summary>Everything known about each output column — the stored column behind it where there is one,
    /// with its declared type and constraints. Described on demand, not per query: only a caller asking for
    /// schema (<c>GetSchemaTable</c>, <c>GetColumnSchema</c>) pays for it.</summary>
    public IReadOnlyList<ResultColumn> Columns =>
        _columns ??= _describe?.Invoke()
        ?? ColumnNames.Select((name, i) => new ResultColumn(name, ColumnTypes[i])).ToList();

    public IReadOnlyList<string> ColumnNames { get; }

    /// <summary>Declared CLR type for each output column. Unlike row-value inference, this remains
    /// available for empty results and when the first runtime value is null.</summary>
    public IReadOnlyList<Type> ColumnTypes { get; }

    /// <summary>Lazily-evaluated rows. Enumerating drives the underlying cursors.</summary>
    public IEnumerable<object?[]> Rows { get; }

    public static ResultSet Empty { get; } = new([], [], []);
}

/// <summary>
/// What is known about one column of a result: its name and CLR type always, and — where the value comes
/// straight from a stored column rather than being computed — that column's table, name, declared type and
/// the constraints on it. The ADO layer turns these into <c>GetSchemaTable</c> rows and <c>DbColumn</c>s.
/// </summary>
/// <param name="Name">The column's name in the result, which an alias in the query does change.</param>
/// <param name="ClrType">The CLR type of the column's values.</param>
/// <param name="BaseTableName">The table the value is read from, or null when nothing stored stands behind it.</param>
/// <param name="BaseColumnName">Its name in that table, which an alias in the query does not change.</param>
/// <param name="AllowNull">Whether the column can hold a null.</param>
/// <param name="IsExpression">Whether the value is computed rather than read from a stored column.</param>
/// <param name="IsAutoIncrement">Whether the stored column behind it is an AutoNumber.</param>
/// <param name="IsKey">Whether it is part of the table's primary key.</param>
/// <param name="IsUnique">Whether a unique index covers it on its own.</param>
/// <param name="IsLong">Whether it is a long value (Memo or OLE Object), stored off-row.</param>
/// <param name="IsReadOnly">Whether the result cannot be written back through.</param>
/// <param name="Size">The declared length, for a sized text or binary column.</param>
/// <param name="Precision">The declared precision, for a Decimal column.</param>
/// <param name="Scale">The declared scale, for a Decimal column.</param>
/// <param name="ProviderType">The OLE DB type code, as the schema collections report it.</param>
/// <param name="TypeName">The provider's name for the type, as the DataTypes collection spells it.</param>
public sealed record ResultColumn(
    string Name,
    Type ClrType,
    string? BaseTableName = null,
    string? BaseColumnName = null,
    bool AllowNull = true,
    bool IsExpression = false,
    bool IsAutoIncrement = false,
    bool IsKey = false,
    bool IsUnique = false,
    bool IsLong = false,
    bool IsReadOnly = false,
    int? Size = null,
    int? Precision = null,
    int? Scale = null,
    int ProviderType = 0,
    string TypeName = "");