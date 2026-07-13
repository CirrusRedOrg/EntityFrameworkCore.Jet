namespace LibRed.Formats;

/// <summary>
/// Layout of the two per-index TDEF sub-structures on the Jet 4 / ACE family: the 52-byte <b>index-data
/// block</b> (column slots, root page, flags) and the 28-byte <b>index-info block</b> (name/data-block linkage
/// and relationship action bytes). Shared by the read side (<c>TableDefinitionPage</c>) and the write sides
/// (<c>TdefBuilder</c>, <c>TableCreator</c>) so the offsets and markers can't drift.
/// </summary>
internal static class IndexBlockFormat
{
    // Index-data block (52 bytes): a 0x783 marker, up to 10 column slots, root page, flags.
    public const int DataBlockSize = 52;
    public const int MaxColumns = 10;
    public const int ColumnSlotSize = 3;          // 2-byte column id + 1-byte flags
    public const int ColumnsOffset = 0x04;
    public const int UsageMapRowOffset = 0x22;    // 1-byte row + 3-byte page for the index's pages
    public const int RootPageOffset = 0x26;
    public const int FlagsOffset = 0x2E;
    public const short ColumnUnused = -1;         // 0xFFFF in an unused column slot
    public const byte ColumnAscending = 0x01;     // slot flags byte: 0x01 = ascending, 0x00 = descending
    public const uint DataMarker = 0x783;

    // Index-info block (28 bytes, one per logical index): links a name to a data block.
    public const int InfoBlockSize = 28;
    public const int InfoMarkerOffset = 0x00;     // 0x0659 record marker
    public const int InfoNumberOffset = 0x04;
    public const int InfoDataNumberOffset = 0x08;
    public const int InfoFkTypeOffset = 0x0C;     // 0=none, 1=incoming, 2=outgoing relationship
    public const int InfoFkNumberOffset = 0x0D;   // 0xFFFFFFFF = no foreign key
    public const int InfoFkTablePageOffset = 0x11; // the other table's TDEF page (0 = none)
    public const int InfoUpdateActionOffset = 0x15;
    public const int InfoDeleteActionOffset = 0x16;
    public const int InfoTypeOffset = 0x17;
    public const uint NoForeignKey = 0xFFFFFFFF;

    /// <summary>Update/delete action byte on a plain (non-relationship) index.</summary>
    public const byte PlainAction = 0x04;

    // Index-info block type byte (at InfoTypeOffset).
    public const byte TypeSecondary = 0x00;
    public const byte TypePrimary = 0x01;
    public const byte TypeForeign = 0x02;
}
