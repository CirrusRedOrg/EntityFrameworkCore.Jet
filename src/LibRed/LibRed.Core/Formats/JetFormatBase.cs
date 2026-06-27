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

        return FromVersionByte(header[VersionOffset]);
    }

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
