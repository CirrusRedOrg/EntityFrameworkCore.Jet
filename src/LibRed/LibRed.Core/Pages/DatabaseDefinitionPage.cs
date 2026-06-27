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
    public string? DatabasePassword { get; internal set; }
    public int DatabaseKey { get; internal set; }
    public short CodePage { get; internal set; }
    public short TextCollateSortOrder { get; internal set; }
    public string? PageKey { get; internal set; }
    public DateTime DatabaseCreationDate { get; internal set; }
    public string? CreateProgramName { get; internal set; }

    public override void Read(PageBuffer buffer)
    {
        PageNumber = buffer.PageNumber;
        FormatIdentifier = Formats.JetFormatBase.ReadFormatIdentifier(buffer.Span);
        JetVersion = buffer.ReadByte(Formats.JetFormatBase.VersionOffset);

        // TODO: decode code page, collation, creation date and encryption material.
        // Everything from offset 0x18 onward on this page is obfuscated/encrypted and the
        // layout differs between Jet 3, Jet 4 and ACE — drive it off the resolved format
        // (see mdbtools mdb_read_db_page / Jackcess DatabaseImpl.readDatabaseDefinition).
    }
}
