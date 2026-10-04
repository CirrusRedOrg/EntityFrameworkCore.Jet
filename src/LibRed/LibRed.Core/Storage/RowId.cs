namespace LibRed.Storage;

/// <summary>A pointer to a row: the data page it lives on and its slot index within that page.</summary>
public readonly record struct RowId(int Page, int Row)
{
    /// <summary>The row as one integer, page in the upper three bytes and row in the lowest: the value an index
    /// entry's trailer carries (big-endian), and a record pointer (little-endian).</summary>
    public int Packed => (Page << 8) | (Row & 0xFF);

    /// <summary>The inverse of <see cref="Packed"/>.</summary>
    public static RowId FromPacked(int packed) => new((int)((uint)packed >> 8), packed & 0xFF);
}