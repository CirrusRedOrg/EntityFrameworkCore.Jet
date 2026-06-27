namespace LibRed.Formats;

/// <summary>
/// Version-specific layout description for a Jet/ACE database. Holds every byte
/// offset, size and limit that differs between format versions, so the page
/// parsers can read named constants instead of hard-coded magic numbers.
/// </summary>
/// <remarks>
/// The authoritative references for these values are the mdbtools source
/// (<c>include/mdbtools.h</c>, <c>src/libmdb/</c>) and Jackcess
/// (<c>com.healthmarketscience.jackcess.impl.JetFormat</c>).
/// </remarks>
public abstract class JetFormatBase
{
    /// <summary>Offset of the one-byte format version marker within page 0.</summary>
    public const int VersionOffset = 0x14;

    /// <summary>Offset of the ASCII format identifier string within page 0.</summary>
    public const int FormatIdentifierOffset = 0x04;

    /// <summary>Length of the format identifier string (excluding its NUL terminator).</summary>
    public const int FormatIdentifierLength = 15;

    /// <summary>Identifier for the MDB (Jet 3/4) family.</summary>
    public const string JetIdentifier = "Standard Jet DB";

    /// <summary>Identifier for the ACCDB (ACE 12+) family.</summary>
    public const string AceIdentifier = "Standard ACE DB";

    // --- Table definition (TDEF) page layout ---
    // Defaults below are for Jet 4 / ACE (verified against a real ACCDB). Jet 3 differs
    // (18-byte column entries, 1-byte ASCII name lengths) and will override these.

    /// <summary>Offset of the 4-byte pointer to the next TDEF page (0 if the definition fits one page).</summary>
    public virtual int TdefNextPageOffset => 0x04;

    /// <summary>Offset of the 4-byte row count.</summary>
    public virtual int TdefRowCountOffset => 0x10;

    /// <summary>Offset of the 1-byte table type (0x4E 'N' user, 0x53 'S' system).</summary>
    public virtual int TdefTableTypeOffset => 0x28;

    /// <summary>Offset of the 2-byte variable-length column count.</summary>
    public virtual int TdefVariableColumnsOffset => 0x2B;

    /// <summary>Offset of the 2-byte total column count.</summary>
    public virtual int TdefColumnCountOffset => 0x2D;

    /// <summary>Offset of the 4-byte real-index (slot) count, used to size the index block before columns.</summary>
    public virtual int TdefRealIndexCountOffset => 0x2F;

    /// <summary>Offset of the 4-byte logical index count.</summary>
    public virtual int TdefIndexCountOffset => 0x33;

    /// <summary>Offset where the real-index block begins; column descriptors follow it.</summary>
    public virtual int TdefRealIndexBlockOffset => 0x3F;

    /// <summary>Size in bytes of each real-index entry in the block before the column descriptors.</summary>
    public virtual int RealIndexEntrySize => 12;

    /// <summary>Size in bytes of one column descriptor.</summary>
    public virtual int ColumnDescriptorSize => 25;

    // --- Column descriptor layout (offsets within a single descriptor) ---
    public virtual int ColumnTypeOffset => 0x00;
    public virtual int ColumnNumberOffset => 0x05;
    public virtual int ColumnFlagsOffset => 0x0F;
    public virtual int ColumnFixedOffsetOffset => 0x15;
    public virtual int ColumnLengthOffset => 0x17;

    /// <summary>Column flag: the column is fixed-length.</summary>
    public const byte ColumnFlagFixedLength = 0x01;

    /// <summary>Column flag: the column is an AutoNumber.</summary>
    public const byte ColumnFlagAutoNumber = 0x04;

    /// <summary>Page size in bytes (2048 for Jet 3, 4096 for Jet 4 and all ACE versions).</summary>
    public int PageSize { get; protected set; } = 4096;

    /// <summary>The logical version this format describes.</summary>
    public abstract JetVersion Version { get; }

    /// <summary>True for the ACCDB (ACE 12+) family, which uses different encryption and layout details.</summary>
    public virtual bool IsAccdb => false;

    /// <summary>
    /// Sniffs the format version byte from page 0 of <paramref name="stream"/> and
    /// returns the matching format description. Restores the stream position.
    /// </summary>
    public static JetFormatBase Detect(Stream stream)
    {
        Span<byte> header = stackalloc byte[VersionOffset + 1];
        long original = stream.Position;
        stream.Seek(0, SeekOrigin.Begin);
        stream.ReadExactly(header);
        stream.Seek(original, SeekOrigin.Begin);

        string identifier = ReadFormatIdentifier(header);
        if (identifier is not (JetIdentifier or AceIdentifier))
            throw new NotSupportedException(
                $"Not a Jet/ACE database: expected \"{JetIdentifier}\" or \"{AceIdentifier}\" at offset 0x{FormatIdentifierOffset:X2}, found \"{identifier}\".");

        return FromVersionByte(header[VersionOffset]);
    }

    /// <summary>Reads the ASCII format identifier ("Standard Jet DB"/"Standard ACE DB") from a page-0 header.</summary>
    public static string ReadFormatIdentifier(ReadOnlySpan<byte> header) =>
        System.Text.Encoding.ASCII.GetString(header.Slice(FormatIdentifierOffset, FormatIdentifierLength)).TrimEnd('\0');

    /// <summary>Maps the raw version byte at <see cref="VersionOffset"/> to a format instance.</summary>
    public static JetFormatBase FromVersionByte(byte versionByte) => versionByte switch
    {
        0x00 => new Jet3Format(),
        0x01 => new Jet4Format(),
        0x02 => new Jet12Format(),
        0x03 => new Jet14Format(),
        0x05 => new Jet16Format(),
        0x06 => new Jet17Format(),
        _ => throw new NotSupportedException($"Unknown Jet/ACE format version byte 0x{versionByte:X2}."),
    };
}
