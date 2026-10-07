using System.Buffers.Binary;
using System.Text;

namespace LibRed.Catalog;

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
        if (fileData.Length < OuterHeaderSize)
            throw new InvalidDataException(
                $"Attachment data is {fileData.Length} bytes; the header alone is {OuterHeaderSize}.");

        var compression = (CompressionKind)BinaryPrimitives.ReadUInt32LittleEndian(
            fileData[CompressionOffset..]);
        uint bodyLength = BinaryPrimitives.ReadUInt32LittleEndian(fileData[BodyLengthOffset..]);

        byte[] body = compression switch
        {
            CompressionKind.None => fileData[OuterHeaderSize..].ToArray(),
            CompressionKind.ZLib => Inflate(fileData[OuterHeaderSize..]),
            _ => throw new InvalidDataException($"Attachment data has compression flag {(uint)compression}; expected 0 or 1."),
        };
        if (body.Length != bodyLength)
            throw new InvalidDataException($"Attachment body is {body.Length} bytes; its header declares {bodyLength}.");
        if (body.Length < MinInnerHeaderSize)
            throw new InvalidDataException(
                $"Attachment body is {body.Length} bytes; its inner header alone is {MinInnerHeaderSize}.");

        int innerLength = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(InnerLengthOffset));
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
                $"An attachment is {content.Length} bytes; an attachment holds at most {MaxAccessAttachmentBytes} bytes.");
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

        // The extension is null-terminated UTF-16, and the unit count is its length INCLUDING that terminator —
        // "pdf" gives 4 and a 20-byte inner header, "thmx" 5 and 22.
        int units = extension.Length + 1;
        int innerLength = ExtensionOffset + units * sizeof(char);

        // The body is the inner header followed by the file; the outer header's length is the body's
        // UNCOMPRESSED size, whether or not the stream that follows is deflated.
        byte[] body = new byte[innerLength + content.Length];
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(InnerLengthOffset), innerLength);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(InnerConstantOffset), InnerHeaderConstant);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(ExtensionUnitsOffset), units);
        Encoding.Unicode.GetBytes(extension, body.AsSpan(ExtensionOffset));   // terminator stays 0
        content.CopyTo(body.AsSpan(innerLength));

        byte[] stored = deflate ? Deflate(body) : body;
        byte[] blob = new byte[OuterHeaderSize + stored.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(CompressionOffset),
            (uint)(deflate ? CompressionKind.ZLib : CompressionKind.None));
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(BodyLengthOffset), (uint)body.Length);
        stored.CopyTo(blob.AsSpan(OuterHeaderSize));
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

    // --- Outer header ---

    internal const int CompressionOffset = 0;
    internal const int BodyLengthOffset = 4;
    internal const int OuterHeaderSize = 8;

    // --- Inner header, from the body's start ---

    internal const int InnerLengthOffset = 0;

    /// <summary>A word every attachment measured carries as <see cref="InnerHeaderConstant"/>; its meaning is
    /// not established.</summary>
    internal const int InnerConstantOffset = 4;
    internal const int InnerHeaderConstant = 1;

    /// <summary>The extension's length in UTF-16 units, its terminator included — "pdf" gives 4.</summary>
    internal const int ExtensionUnitsOffset = 8;
    internal const int ExtensionOffset = 12;

    /// <summary>The inner header of a three-letter extension, the shortest measured.</summary>
    internal const int MinInnerHeaderSize = 20;

    private enum CompressionKind : uint
    {
        None = 0,
        ZLib = 1,
    }

}