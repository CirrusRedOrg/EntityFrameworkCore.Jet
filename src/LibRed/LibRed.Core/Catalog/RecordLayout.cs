using LibRed.Formats;

namespace LibRed.Catalog;

/// <summary>
/// Validates a column declaration against the two width limits ACE enforces when it opens a file. LibRed
/// writes the format directly, bypassing ACE's DDL parser, so without these it emits tables ACE refuses —
/// both measured against ACE (OLE DB) 2026-09-06, and both in the same way as the name rules in
/// <see cref="JetName"/>: the damage is to the whole database, not the one table.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per-field width.</b> A non-long-value column holds at most <see cref="MaxFieldBytes"/> bytes — 255
/// characters of Text, or 510 bytes of Binary — fixed or variable alike. ACE refuses the declaration
/// outright through its own DDL (<c>TEXT(256)</c> and <c>BINARY(511)</c> both give "Size of field is too
/// long", on <c>CREATE TABLE</c>, <c>ALTER COLUMN</c> and <c>ADD COLUMN</c>), and a file LibRed writes with
/// a wider column opens but cannot be queried: <c>SELECT</c> fails with "The size of a field is too long".
/// </para>
/// <para>
/// <b>Declared record size.</b> The widest record the declaration permits must fit
/// <see cref="JetFormatBase.MaxRecordSize"/>. ACE budgets the fixed region, the null bitmap and the
/// variable section's own overhead — but <i>not</i> the declared widths of the variable columns, which is
/// why an all-Text table is unconstrained while a wide fixed region is not. Measured at two very different
/// column counts, 31 bytes apart exactly as the differing bitmap widths predict:
/// </para>
/// <list type="bullet">
///   <item>8 fixed columns: 4053 fixed bytes opens, 4054 gives "Unrecognized database format".</item>
///   <item>252 fixed columns: 4022 opens, 4023 does not.</item>
///   <item>4018 fixed bytes with 0, 1 and 2 variable columns opens; a third takes it past and fails.</item>
/// </list>
/// <para>
/// The 4-byte allowance for a table with no variable columns at all is ACE's, not a row LibRed writes —
/// <see cref="Storage.RowEncoder"/> omits the variable section entirely in that case, so LibRed's own rows
/// come in 4 bytes under what ACE reserves for them. See <c>docs/format/page-02b-columns.md</c>.
/// </para>
/// </remarks>
public static class RecordLayout
{
    /// <summary>The widest a single non-Memo/OLE column may be declared: 255 Text characters, or 510 bytes
    /// of Binary.</summary>
    public const int MaxFieldBytes = 510;

    /// <summary>Throws if a column is declared wider than ACE stores. Memo and OLE are exempt — their data
    /// lives on long-value pages and the in-row descriptor is a fixed size.</summary>
    public static void ValidateFieldWidth(string columnName, JetDataType type, int lengthBytes)
    {
        if (type is JetDataType.Memo or JetDataType.Ole) return;
        if (lengthBytes > MaxFieldBytes)
            throw new NotSupportedException(
                $"Column '{columnName}' is declared {lengthBytes} bytes wide; Jet/ACE stores at most {MaxFieldBytes} "
                + $"per field ({MaxFieldBytes / 2} Text characters). Use Memo or OLE for anything longer — a wider "
                + "column leaves a table Access cannot query.");
    }

    /// <summary>The largest record the declaration can produce: the row header, the fixed region, the
    /// variable section's overhead and the null bitmap. Mirrors <see cref="Storage.RowEncoder"/>'s layout
    /// except that a table with no variable columns still gets ACE's 4-byte allowance for the section.</summary>
    /// <param name="fixedBytes">Sum of the fixed columns' widths, excluding Boolean (which has no data).</param>
    /// <param name="variableColumns">Number of variable-length columns, Memo and OLE included.</param>
    /// <param name="columnCount">The column-id high-water plus one — what sizes the null bitmap.</param>
    public static int WidestRecord(int fixedBytes, int variableColumns, int columnCount) =>
        2                                                                   // leading column count
        + fixedBytes
        + (variableColumns == 0 ? 4 : (variableColumns + 1) * 2 + 2)        // offset table + the count
        + (columnCount + 7) / 8;                                            // null bitmap

    /// <summary>Throws if the declaration's widest possible record is one ACE would refuse the file for.
    /// <paramref name="tableName"/> may be null where the caller does not know it (the table is still being
    /// built), in which case the message just says "the table".</summary>
    public static void ValidateRecordFits(
        string? tableName, int fixedBytes, int variableColumns, int columnCount, JetFormatBase format)
    {
        int widest = WidestRecord(fixedBytes, variableColumns, columnCount);
        if (widest > format.MaxRecordSize)
            throw new NotSupportedException(
                $"{(tableName is null ? "The table" : $"Table '{tableName}'")} declares {fixedBytes} bytes of "
                + $"fixed-length columns over {columnCount} column ids, so its widest record would be {widest} "
                + $"bytes and Jet/ACE stores at most {format.MaxRecordSize}. Access cannot open a database "
                + "containing such a table at all. Make the wide columns variable-length, or move them to "
                + "Memo/OLE, which live on their own pages.");
    }
}
