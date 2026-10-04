namespace LibRed.Formats;

/// <summary>The status bits of a data page's row slot directory entry, above its
/// <see cref="JetFormatBase.DataRowOffsetMask"/> offset.</summary>
[Flags]
internal enum RowSlotFlags
{
    None = 0,

    /// <summary>The slot holds a forward pointer, not an inline row: a live one begins with the record pointer to
    /// where its row was relocated (the new home is Deleted with this flag clear). With <see cref="Deleted"/>, a
    /// zero-length tombstone.</summary>
    Overflow = 0x4000,

    /// <summary>The row is deleted (a tombstone).</summary>
    Deleted = 0x8000,
}