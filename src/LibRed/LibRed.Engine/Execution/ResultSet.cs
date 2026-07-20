namespace LibRed.Engine.Execution;

/// <summary>
/// The shape and rows produced by executing a query plan. Rows are arrays of boxed
/// values aligned to <see cref="ColumnNames"/>; the ADO layer projects these into a
/// <c>DbDataReader</c>.
/// </summary>
public sealed class ResultSet
{
    public ResultSet(
        IReadOnlyList<string> columnNames,
        IEnumerable<object?[]> rows,
        IReadOnlyList<Type>? columnTypes = null)
    {
        if (columnTypes is not null && columnTypes.Count != columnNames.Count)
            throw new ArgumentException("The number of column types must match the number of column names.", nameof(columnTypes));

        ColumnNames = columnNames;
        Rows = rows;
        ColumnTypes = columnTypes ?? Enumerable.Repeat(typeof(object), columnNames.Count).ToArray();
    }

    public IReadOnlyList<string> ColumnNames { get; }

    /// <summary>Declared CLR type for each output column. Unlike row-value inference, this remains
    /// available for empty results and when the first runtime value is null.</summary>
    public IReadOnlyList<Type> ColumnTypes { get; }

    /// <summary>Lazily-evaluated rows. Enumerating drives the underlying cursors.</summary>
    public IEnumerable<object?[]> Rows { get; }

    public static ResultSet Empty { get; } = new([], [], []);
}
