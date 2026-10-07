namespace LibRed.Formats;

/// <summary>
/// The column flag byte (<see cref="JetFormatBase.ColumnFlagsOffset"/>). The user-column bits are modelled (read
/// into ColumnDef, written from it); the two catalog bits travel as a created column's raw
/// <c>SystemFlags</c>, and 0x08 is set on no column seen.
/// </summary>
[Flags]
internal enum ColumnFlags : byte
{
    None = 0,

    /// <summary>The column is fixed-length.</summary>
    FixedLength = 0x01,

    /// <summary>The column is updatable (set on essentially every column).</summary>
    Updatable = 0x02,

    /// <summary>The column is an AutoNumber.</summary>
    AutoNumber = 0x04,

    /// <summary>A column of the engine's own catalog — every column of MSysObjects, MSysACEs, MSysQueries,
    /// MSysRelationships and MSysComplexColumns, and no other.</summary>
    SystemCatalog = 0x10,

    /// <summary>A catalog column holding a security identifier (MSysObjects.Owner, MSysACEs.SID), always with
    /// <see cref="SystemCatalog"/>.</summary>
    SecurityId = 0x20,

    /// <summary>An AutoNumber column that generates GUIDs (Replication ID) rather than Longs.</summary>
    GuidAutoNumber = 0x40,

    /// <summary>A hyperlink (a Memo column presented as a hyperlink).</summary>
    Hyperlink = 0x80,
}

/// <summary>The extended column flag byte (<see cref="JetFormatBase.ColumnExtendedFlagsOffset"/>).</summary>
[Flags]
internal enum ColumnExtendedFlags : byte
{
    None = 0,

    /// <summary>The column can store compressed Unicode text (§7).</summary>
    CompressedUnicode = 0x01,

    /// <summary>An attachment value column (FileData, FileFlags, FileName, FileTimeStamp, FileType, FileURL), in
    /// the attachment template and every attachment flat table.</summary>
    AttachmentValue = 0x10,

    /// <summary>A calculated (computed) column (ACE 14+): the 0xC0 pair, either bit set.</summary>
    Calculated = 0xC0,
}