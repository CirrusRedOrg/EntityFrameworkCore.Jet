using System.Buffers.Binary;
using LibRed.IO;

namespace LibRed.Pages;

/// <summary>
/// Page 0 — the database definition page. Carries the format version, code page,
/// collation, creation metadata and the encryption material needed to decrypt the
/// rest of the file.
/// </summary>
public sealed class DatabaseDefinitionPage : Page
{
    public override PageType Type => PageType.DatabaseDefinition;

    /// <summary>The ASCII format identifier, e.g. "Standard Jet DB" or "Standard ACE DB".</summary>
    public string FormatIdentifier { get; internal set; } = string.Empty;

    public byte JetVersion { get; internal set; }

    /// <summary>The 4-byte database (encryption) key; 0 when the database has no password.</summary>
    public int DatabaseKey { get; internal set; }

    /// <summary>The database's ANSI code page (e.g. 1252 Latin-1, 1250 Central European),
    /// decoded from the obfuscated field at <see cref="Formats.JetFormatBase.CodePageOffset"/>.</summary>
    public int CodePage { get; internal set; }

    /// <summary>The database's default collation LCID (e.g. 1033 = en-US), decoded from the
    /// obfuscated sort-order field at <see cref="Formats.JetFormatBase.CollationSortOrderOffset"/>.</summary>
    public int DefaultCollationLcid { get; internal set; }

    /// <summary>The database default sort-order version: 0 = General Legacy, 1 = General (2010+).
    /// Matches each column descriptor's byte <c>0x0E</c>.</summary>
    public byte DefaultCollationVersion { get; internal set; }

    /// <summary>Page number of the <c>MSysObjects</c> TDEF (the catalog root), read from the bootstrap
    /// pointer at <see cref="Formats.JetFormatBase.CatalogRootPointerOffset"/>. 2 in every observed file.</summary>
    public int CatalogRootPage { get; internal set; }

    public DateTime DatabaseCreationDate { get; internal set; }

    public override void Read(PageBuffer buffer, Formats.JetFormatBase format)
    {
        PageNumber = buffer.PageNumber;
        FormatIdentifier = Formats.JetFormatBase.ReadFormatIdentifier(buffer.Span);
        JetVersion = buffer.ReadByte(Formats.JetFormatBase.VersionOffset);

        // The header from 0x18 is XOR-obfuscated with the fixed 128-byte mask; de-obfuscate the
        // whole region once, then read the fields out of the clear copy.
        Span<byte> clear = stackalloc byte[Formats.JetFormatBase.PageZeroHeaderMask.Length];
        Demask(buffer.Span, clear);
        int b = Formats.JetFormatBase.PageZeroHeaderMaskStart;

        CodePage = BinaryPrimitives.ReadUInt16LittleEndian(clear.Slice(Formats.JetFormatBase.CodePageOffset - b, 2));
        DatabaseKey = BinaryPrimitives.ReadInt32LittleEndian(clear.Slice(Formats.JetFormatBase.DatabaseKeyOffset - b, 4));
        DefaultCollationLcid = BinaryPrimitives.ReadUInt16LittleEndian(clear.Slice(Formats.JetFormatBase.CollationSortOrderOffset - b, 2));
        DefaultCollationVersion = clear[Formats.JetFormatBase.CollationVersionOffset - b];
        CatalogRootPage = BinaryPrimitives.ReadInt32LittleEndian(clear.Slice(Formats.JetFormatBase.CatalogRootPointerOffset - b, 4));
        double days = BinaryPrimitives.ReadDoubleLittleEndian(clear.Slice(Formats.JetFormatBase.CreationDateOffset - b, 8));
        DatabaseCreationDate = OleAutomationEpoch.AddDays(days);
    }

    /// <summary>XOR-de-obfuscates the page-0 header region into <paramref name="clear"/>, whose length
    /// equals the mask length; <c>clear[i]</c> corresponds to page offset
    /// <see cref="Formats.JetFormatBase.PageZeroHeaderMaskStart"/><c> + i</c>.</summary>
    internal static void Demask(ReadOnlySpan<byte> page, Span<byte> clear)
    {
        ReadOnlySpan<byte> mask = Formats.JetFormatBase.PageZeroHeaderMask;
        int start = Formats.JetFormatBase.PageZeroHeaderMaskStart;
        for (int i = 0; i < mask.Length; i++)
            clear[i] = (byte)(page[start + i] ^ mask[i]);
    }

    /// <summary>The OLE automation date epoch: 1899-12-30.</summary>
    private static readonly DateTime OleAutomationEpoch = new(1899, 12, 30);
}
