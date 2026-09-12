namespace LibRed.Catalog;

/// <summary>
/// The column data types supported by Jet/ACE, with their on-disk type codes.
/// <para>The <c>Unknown*</c> members are placeholders for codes in the range that LibRed does not model.
/// They exist so that meeting one is <b>not fatal</b>: the TDEF still parses, the column decodes to its raw
/// bytes (<see cref="Storage.Types.JetTypeCodec"/>'s default arm), and only *writing* one is refused. Without
/// them a single unrecognised column made the entire database unopenable, because the catalog reads every
/// table's definition — the same shape of failure a long-value guard once caused (page-02b §3.4a).</para>
/// </summary>
public enum JetDataType : byte
{
    Boolean = 0x01,
    Byte = 0x02,
    Int16 = 0x03,
    Int32 = 0x04,
    Currency = 0x05,
    Single = 0x06,
    Double = 0x07,
    DateTime = 0x08,
    Binary = 0x09,
    Text = 0x0A,
    Ole = 0x0B,
    Memo = 0x0C,

    /// <summary>Unmodelled. Not seen in any file measured — a gap in the sequence, held open so the code
    /// cannot take a database down if one turns up.</summary>
    Unknown0D = 0x0D,

    /// <summary>Unmodelled. Not seen in any file measured — see <see cref="Unknown0D"/>.</summary>
    Unknown0E = 0x0E,

    Guid = 0x0F,
    FixedPoint = 0x10, // NUMERIC / DECIMAL

    /// <summary>Unmodelled, and the one unmodelled code actually met in the wild: three real Jet 4 `.mdb`
    /// files carry it, every time as <c>MSysAccessObjects.Data</c> — fixed length, 3992 bytes, holding chunks
    /// of an OLE Compound File (signature <c>D0 CF 11 E0 A1 B1 1A E1</c>). That is Access's own object
    /// storage: the VBA project and its type-library references. Never seen on a user column. LibRed hands
    /// back the raw bytes and does not interpret the container.</summary>
    Unknown11 = 0x11,

    Complex = 0x12,    // ACE complex/multi-value columns
    Int64 = 0x13,      // ACE 16 (Access 2016, version byte 0x05): BIGINT (Large Number)
    DateTimeExtended = 0x14, // ACE 17 (Access 2019+, version byte 0x06): DATETIME2 (Date/Time Extended)
}
