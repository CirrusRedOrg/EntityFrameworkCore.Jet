using System.Buffers.Binary;
using System.Text;

namespace LibRed.Catalog;

/// <summary>
/// A complex (multi-value / attachment) column and the per-column table its values live in. The column's
/// in-row value is a 4-byte <b>complex id</b> naming one record; the values for that record are the rows of
/// <see cref="FlatTable"/> whose <see cref="OwnerLink"/> holds the id.
/// </summary>
/// <remarks>
/// <para>Everything here is resolved <b>structurally</b>, never by building names. The flat table and its two
/// bookkeeping columns keep whatever the table and column were called when the complex column was created, and
/// a later rename does not follow: a column now called <c>BK_category</c> can be backed by
/// <c>f_…_TempField*7</c>, and a table now called <c>Borrow</c> can have a value-id column still named
/// <c>Table1_BRW_book</c>. The flat table comes from <c>MSysComplexColumns.FlatTableID</c> and the two columns
/// from the index shape. See <c>docs/format/system-catalog.md</c>.</para>
/// <para>A record's id is allocated when the <b>row</b> is created, so a non-null id is no evidence that any
/// value exists — an empty <see cref="Read"/> is the ordinary answer, not an error.</para>
/// </remarks>
public sealed class ComplexColumn
{
    internal ComplexColumn(
        string columnName, int complexId, TableDef ownerTable, TableDef flatTable,
        ColumnDef ownerLink, ColumnDef valueId, string? elementTypeName)
    {
        ColumnName = columnName;
        ComplexId = complexId;
        OwnerTable = ownerTable;
        FlatTable = flatTable;
        OwnerLink = ownerLink;
        ValueId = valueId;
        ElementTypeName = elementTypeName;
        ValueColumns = [.. flatTable.Columns.Where(c => c != ownerLink && c != valueId)];
    }

    /// <summary>The complex column's name on <see cref="OwnerTable"/>.</summary>
    public string ColumnName { get; }

    /// <summary>Its <c>MSysComplexColumns.ComplexID</c> — the same value the column descriptor carries at
    /// <c>0x0B</c>.</summary>
    public int ComplexId { get; }

    /// <summary>The table holding the complex column.</summary>
    public TableDef OwnerTable { get; }

    /// <summary>The per-column table holding the values, one row each.</summary>
    public TableDef FlatTable { get; }

    /// <summary>The flat table's link back to the owning record: it holds that record's complex id and
    /// <b>repeats once per value</b>, which is what makes the column multi-valued. Indexed, not unique.</summary>
    public ColumnDef OwnerLink { get; }

    /// <summary>The flat table's per-value id: unique, primary, and an ordinary AutoNumber whose high-water
    /// is the flat table's own TDEF <c>0x14</c>.</summary>
    public ColumnDef ValueId { get; }

    /// <summary>The value columns proper — everything but the two bookkeeping ones. A scalar multi-value
    /// column has a single <c>Value</c>; an attachment has the six <c>File*</c> columns.</summary>
    public IReadOnlyList<ColumnDef> ValueColumns { get; }

    /// <summary>The <c>MSysComplexType_*</c> template this column was made from (e.g.
    /// <c>MSysComplexType_Attachment</c>), or null when the catalog row does not resolve to one.</summary>
    public string? ElementTypeName { get; }

    /// <summary>Whether this is an attachment column rather than a multi-value scalar — it carries the
    /// <c>FileData</c>/<c>FileName</c> shape, so <see cref="ComplexAttachment.Unwrap"/> applies.</summary>
    public bool IsAttachment =>
        FlatTable.FindColumn("FileData") is not null && FlatTable.FindColumn("FileName") is not null;
}

/// <summary>An attachment's stored file: the extension Access recorded, and the bytes themselves.</summary>
/// <param name="Extension">The extension from the payload's inner header (<c>pdf</c>, <c>png</c>, …), without
/// a dot. This is the payload's own copy, which need not equal the <c>FileType</c> column.</param>
/// <param name="Content">The file's bytes, decompressed where Access compressed them.</param>
public readonly record struct ComplexAttachment(string Extension, byte[] Content)
{
    /// <summary>
    /// Unwraps the <c>FileData</c> column of an attachment row. The stored blob is an 8-byte header — a
    /// compression flag (<c>1</c> = a zlib stream follows, <c>0</c> = raw bytes) and the body's decompressed
    /// length — then the body, which itself opens with a 20-byte header carrying the extension as
    /// null-terminated UTF-16. Verified against a pdf, an mp3 and a png; see
    /// <c>docs/format/system-catalog.md</c>.
    /// </summary>
    /// <exception cref="InvalidDataException">The blob is too short, or its headers do not describe it.</exception>
    public static ComplexAttachment Unwrap(ReadOnlySpan<byte> fileData)
    {
        const int OuterHeader = 8, InnerHeaderLength = 20, ExtensionOffset = 12;
        if (fileData.Length < OuterHeader)
            throw new InvalidDataException($"Attachment data is {fileData.Length} bytes; the header alone is {OuterHeader}.");

        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(fileData);
        uint bodyLength = BinaryPrimitives.ReadUInt32LittleEndian(fileData[4..]);

        byte[] body = compression switch
        {
            0 => fileData[OuterHeader..].ToArray(),
            1 => Inflate(fileData[OuterHeader..]),
            _ => throw new InvalidDataException($"Attachment data has compression flag {compression}; expected 0 or 1."),
        };
        if (body.Length != bodyLength)
            throw new InvalidDataException($"Attachment body is {body.Length} bytes; its header declares {bodyLength}.");
        if (body.Length < InnerHeaderLength)
            throw new InvalidDataException($"Attachment body is {body.Length} bytes; its inner header alone is {InnerHeaderLength}.");

        int innerLength = BinaryPrimitives.ReadInt32LittleEndian(body);
        if (innerLength < ExtensionOffset || innerLength > body.Length)
            throw new InvalidDataException($"Attachment inner header declares {innerLength} bytes, which its {body.Length}-byte body cannot hold.");

        string extension = Encoding.Unicode
            .GetString(body.AsSpan(ExtensionOffset, innerLength - ExtensionOffset)).TrimEnd('\0');
        return new ComplexAttachment(extension, body[innerLength..]);
    }

    /// <summary>
    /// The extensions Access leaves uncompressed because the format already is. From the documented list
    /// (Attachment object, "Types of files that Access compresses"), which is explicitly partial — anything
    /// absent is compressed, so this set is the exception rather than the rule.
    /// </summary>
    // The comparer is explicit because an extension matches case-insensitively (.PNG is still a png), which
    // IDE0028's collection expression would leave to the default.
#pragma warning disable IDE0028
    private static readonly HashSet<string> NativelyCompressed = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "gif", "png", "zip", "cab", "docx", "xlsx", "xlsb", "pptx",
    };
#pragma warning restore IDE0028

    /// <summary>
    /// The largest attachment <b>Access</b> accepts — "Individual files cannot exceed 256 megabytes". This is
    /// Access's policy, not the format's capability: a long value's stored length runs to <c>0x3FFFFFFF</c>,
    /// which ACE itself accepts and reads back, so the file format holds four times this.
    /// </summary>
    public const int MaxAccessAttachmentBytes = 256 * 1024 * 1024;

    /// <summary>
    /// Whether <b>Access</b> would deflate a file of this extension rather than store it as it is — its
    /// convention, not a rule of the format. Both forms are valid on disk and ACE reads either, so this only
    /// decides whether a file LibRed writes looks like one Access wrote.
    /// </summary>
    public static bool Compresses(string extension) =>
        !NativelyCompressed.Contains((extension ?? "").TrimStart('.'));

    /// <summary>
    /// Throws when <paramref name="fileName"/> or <paramref name="content"/> breaks one of <b>Access's</b>
    /// documented attachment limits — the 256 MB per file, the 255-character name, and the characters a name
    /// may not contain. None of these is a format limit; they are what the Access UI enforces, and a file
    /// breaking them is one Access would not have created and may not open.
    /// </summary>
    public static void ValidateAccessLimits(string fileName, ReadOnlySpan<byte> content)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        if (content.Length > MaxAccessAttachmentBytes)
            throw new ArgumentOutOfRangeException(nameof(content),
                $"An attachment is {content.Length} bytes; Access accepts at most {MaxAccessAttachmentBytes} per file.");
        if (fileName.Length > 255)
            throw new ArgumentException(
                $"An attachment name is at most 255 characters including the extension; '{fileName}' is {fileName.Length}.",
                nameof(fileName));
        if (fileName.IndexOfAny(['?', '"', '/', '\\', '<', '>', '*', '|', ':']) >= 0)
            throw new ArgumentException(
                $"An attachment name cannot contain any of ? \" / \\ < > * | : — '{fileName}' does.",
                nameof(fileName));
    }

    /// <summary>
    /// Builds the <c>FileData</c> blob for an attachment — the inverse of <see cref="Unwrap"/>.
    /// </summary>
    /// <remarks>
    /// <b>Both storage forms are valid and ACE reads either</b>, so compression is a choice, not a
    /// correctness question. Left to <see cref="Compresses"/> it follows Access's own convention —
    /// "Access will compress your attached files unless those files are compressed natively" — which matches
    /// what ACE wrote in the sample file: a <c>png</c> raw, a <c>pdf</c> and an <c>mp3</c> deflated.
    /// </remarks>
    /// <param name="extension">The file's extension without a dot, as the inner header records it.</param>
    /// <param name="content">The file's bytes.</param>
    /// <param name="compress">Force the stored form, or <see langword="null"/> to follow Access's convention
    /// for the extension.</param>
    public static byte[] Pack(string extension, ReadOnlySpan<byte> content, bool? compress = null)
    {
        ArgumentNullException.ThrowIfNull(extension);
        bool deflate = compress ?? Compresses(extension);
        const int OuterHeader = 8, ExtensionOffset = 12;

        // The extension is null-terminated UTF-16, and the count at [8] is its length in UTF-16 units
        // INCLUDING that terminator — "pdf" gives 4 and a 20-byte inner header, "thmx" 5 and 22.
        int units = extension.Length + 1;
        int innerLength = ExtensionOffset + units * 2;

        // The body is the inner header followed by the file; the outer header's length is the body's
        // UNCOMPRESSED size, whether or not the stream that follows is deflated.
        byte[] body = new byte[innerLength + content.Length];
        BinaryPrimitives.WriteInt32LittleEndian(body, innerLength);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8), units);
        Encoding.Unicode.GetBytes(extension, body.AsSpan(ExtensionOffset));                   // terminator stays 0
        content.CopyTo(body.AsSpan(innerLength));

        byte[] stored = deflate ? Deflate(body) : body;
        byte[] blob = new byte[OuterHeader + stored.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(blob, deflate ? 1u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(4), (uint)body.Length);
        stored.CopyTo(blob.AsSpan(OuterHeader));
        return blob;
    }

    private static byte[] Deflate(byte[] body)
    {
        using var output = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(
            output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(body);
        return output.ToArray();
    }

    private static byte[] Inflate(ReadOnlySpan<byte> stream)
    {
        using var input = new MemoryStream(stream.ToArray());
        using var zlib = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}
