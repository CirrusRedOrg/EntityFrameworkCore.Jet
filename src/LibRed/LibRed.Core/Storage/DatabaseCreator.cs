using System.Buffers.Binary;
using System.Text;
using LibRed.Formats;

namespace LibRed.Storage;

/// <summary>
/// Builds the pages of a brand-new, empty Jet/ACE database from scratch — the native, cross-platform
/// replacement for the DAO/ADOX file creator. Currently synthesises page 0 (the database definition
/// page); the system catalog follows.
/// </summary>
public static class DatabaseCreator
{
    private static readonly DateTime OleEpoch = new(1899, 12, 30);

    /// <summary>
    /// Synthesises page 0 (the database definition page) — the exact inverse of
    /// <see cref="Pages.DatabaseDefinitionPage.Read"/>. Byte-for-byte identical to a real empty file's
    /// page 0 for the same parameters (verified against Access-created files).
    /// </summary>
    /// <param name="version">Format version byte (e.g. 0x02 = ACE 12 / Access 2007).</param>
    /// <param name="isAccdb">true for the ACCDB identifier, false for the MDB (Jet) identifier.</param>
    /// <param name="codePage">ANSI code page (1252 for en-US).</param>
    /// <param name="collationLcid">Default collation LCID (1033 = en-US).</param>
    /// <param name="collationVersion">Sort-order version (0 = General Legacy, 1 = General).</param>
    /// <param name="creationDate">Database creation timestamp.</param>
    public static byte[] BuildDefinitionPage(
        byte version, bool isAccdb, int codePage, int collationLcid, byte collationVersion, DateTime creationDate)
    {
        var page = new byte[4096];

        // --- Pre-mask region (0x00..0x17, cleartext) ---
        page[0x00] = 0x00;              // page type
        page[0x01] = 0x01;              // observed constant 01 00 00
        string id = isAccdb ? JetFormatBase.AceIdentifier : JetFormatBase.JetIdentifier;
        Encoding.ASCII.GetBytes(id).CopyTo(page, JetFormatBase.FormatIdentifierOffset); // 0x04, 15 bytes; 0x13 stays NUL
        page[JetFormatBase.VersionOffset] = version;                                     // 0x14
        page[0x15] = (byte)(version == 0x03 ? 0x01 : 0x00);                              // 2010-format minor byte

        // --- Masked header (0x18..0x97): build the clear image, then XOR the fixed mask over it. ---
        int b = JetFormatBase.PageZeroHeaderMaskStart;
        Span<byte> clear = stackalloc byte[JetFormatBase.PageZeroHeaderMask.Length];

        BinaryPrimitives.WriteInt32LittleEndian(clear[(0x18 - b)..], 0x00000100);        // 0x18 fixed constant
        BinaryPrimitives.WriteInt32LittleEndian(clear[(0x1C - b)..], 0x00000101);        // 0x1C fixed constant
        // 0x20..0x2C: system-catalog bootstrap pointers = MSysObjects/ACEs/Queries/Relationships pages.
        BinaryPrimitives.WriteInt32LittleEndian(clear[(0x20 - b)..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(clear[(0x24 - b)..], 3);
        BinaryPrimitives.WriteInt32LittleEndian(clear[(0x28 - b)..], 4);
        BinaryPrimitives.WriteInt32LittleEndian(clear[(0x2C - b)..], 5);
        BinaryPrimitives.WriteUInt16LittleEndian(clear[(JetFormatBase.CodePageOffset - b)..], (ushort)codePage); // 0x3C
        // 0x3E database key = 0 (unencrypted); leave clear zero.
        // 0x42..0x69 password (empty): on disk the field is additionally masked by a 4-byte value derived
        // from the creation date, so the clear image (pre-header-mask) of an empty password is that value
        // repeated — reproduce it exactly.
        double days = (creationDate - OleEpoch).TotalDays;
        Span<byte> dateMask = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(dateMask, (int)days);
        for (int i = 0; i < 40; i++)
            clear[JetFormatBase.PasswordOffset - b + i] = dateMask[i % 4];
        // 0x6A fixed sentinel constant.
        BinaryPrimitives.WriteInt32LittleEndian(clear[(0x6A - b)..], 0x000011A6);
        // 0x6E..0x71 collating sort order: LCID + version byte at 0x71.
        BinaryPrimitives.WriteUInt16LittleEndian(clear[(JetFormatBase.CollationSortOrderOffset - b)..], (ushort)collationLcid);
        clear[JetFormatBase.CollationVersionOffset - b] = collationVersion;
        // 0x72..0x79 creation date (OLE double).
        BinaryPrimitives.WriteDoubleLittleEndian(clear[(JetFormatBase.CreationDateOffset - b)..], days);

        ReadOnlySpan<byte> mask = JetFormatBase.PageZeroHeaderMask;
        for (int i = 0; i < mask.Length; i++)
            page[b + i] = (byte)(clear[i] ^ mask[i]);

        // --- Post-mask tail (cleartext) ---
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(0x98), 0x00000654);           // fixed constant
        Encoding.ASCII.GetBytes("4.0").CopyTo(page, 0x9C);                                 // engine version string

        // TODO: page 0 also carries a 512-byte structure at 0xE00–0xFFF (256 two-byte entries, almost all
        // 0x0100, a small per-file-varying head) — a free-space/usage-summary map, undecoded. LibRed does
        // not read it, so a file is LibRed-openable without it; reproduce it for full Access fidelity.
        // TODO: the system catalog (MSysObjects/ACEs/Queries/Relationships TDEFs + seed rows) — the rest
        // of a bootable file — is built by the catalog synthesiser (still to come).

        return page;
    }
}
