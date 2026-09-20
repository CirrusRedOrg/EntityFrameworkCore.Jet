using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// The standard's <c>LISTAGG(x [, separator]) WITHIN GROUP (ORDER BY …)</c>: the non-Null values of a group or a
/// window frame as text, in the WITHIN GROUP order, joined by the separator (none when it is left out). Access has no
/// such aggregate; this is a LibRed extension.
/// </summary>
internal static class ListAgg
{
    /// <summary>
    /// The list over <paramref name="rows"/> — each a value and its WITHIN GROUP key values — or Null when no value
    /// is present. Each value is written as <c>&amp;</c> writes it. Rows whose keys tie keep their order. Under
    /// <paramref name="distinct"/> a value repeated — equal as GROUP BY takes values to be equal — is listed once,
    /// where it first comes.
    /// </summary>
    public static string? Of(
        IEnumerable<(object? Value, object?[] Keys)> rows, string separator, IReadOnlyList<SortDirection> directions,
        bool distinct)
    {
        var comparer = Comparer<object?[]>.Create((a, b) =>
        {
            for (int k = 0; k < directions.Count; k++)
            {
                int c = ExpressionEvaluator.CompareForSort(a[k], b[k]);
                if (c != 0)
                    return directions[k] == SortDirection.Descending ? -c : c;
            }
            return 0;
        });
        var values = rows.Where(r => r.Value is not null).OrderBy(r => r.Keys, comparer).Select(r => r.Value!);
        if (distinct)
        {
            var seen = new HashSet<QueryExecutor.GroupKey>();
            values = values.Where(v => seen.Add(new QueryExecutor.GroupKey([v])));
        }

        var text = values.Select(ExpressionEvaluator.ConcatText).ToList();
        return text.Count == 0 ? null : string.Join(separator, text);
    }
}
