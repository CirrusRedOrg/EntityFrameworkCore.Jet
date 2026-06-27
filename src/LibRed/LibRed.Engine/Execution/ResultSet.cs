namespace LibRed.Engine.Execution;

/// <summary>
/// The shape and rows produced by executing a query plan. Rows are arrays of boxed
/// values aligned to <see cref="ColumnNames"/>; the ADO layer projects these into a
/// <c>DbDataReader</c>.
/// </summary>
public sealed class ResultSet(IReadOnlyList<string> columnNames, IEnumerable<object?[]> rows)
{
    public IReadOnlyList<string> ColumnNames { get; } = columnNames;

    /// <summary>Lazily-evaluated rows. Enumerating drives the underlying cursors.</summary>
    public IEnumerable<object?[]> Rows { get; } = rows;

    public static ResultSet Empty { get; } = new([], []);
}
