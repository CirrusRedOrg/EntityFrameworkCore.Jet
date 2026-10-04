using System.Buffers.Binary;
using System.Text;
using LibRed.Storage;

namespace LibRed.IO;

/// <summary>
/// A thin, allocation-free reader over a single page's bytes. All Jet/ACE
/// integers are little-endian; the helpers here centralise that so the page
/// parsers can read named offsets instead of scattering <see cref="BitConverter"/>
/// calls everywhere.
/// </summary>
public readonly struct PageBuffer(ReadOnlyMemory<byte> data, int pageNumber)
{
    public ReadOnlyMemory<byte> Data { get; } = data;
    public int PageNumber { get; } = pageNumber;
    public int Length => Data.Length;

    public ReadOnlySpan<byte> Span => Data.Span;

    public byte ReadByte(int offset) => Span[offset];

    public short ReadInt16(int offset) => BinaryPrimitives.ReadInt16LittleEndian(Span.Slice(offset, 2));

    public ushort ReadUInt16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(Span.Slice(offset, 2));

    public int ReadInt32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(Span.Slice(offset, 4));

    public uint ReadUInt32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(Span.Slice(offset, 4));

    public long ReadInt64(int offset) => BinaryPrimitives.ReadInt64LittleEndian(Span.Slice(offset, 8));

    /// <summary>Reads a record pointer: a 1-byte row, then the 3-byte little-endian page holding it. Every usage
    /// map is addressed this way — the TDEF's own, each index's and each long-value column's — and so is a long
    /// value's first chunk and each chunk's next.</summary>
    public (int Row, int Page) ReadRecordPointer(int offset) => ReadRecordPointer(Span, offset);

    /// <summary><see cref="ReadRecordPointer(int)"/> over bytes that are not a whole page: <see cref="RowId.Packed"/>,
    /// stored little-endian.</summary>
    public static (int Row, int Page) ReadRecordPointer(ReadOnlySpan<byte> buffer, int offset)
    {
        RowId id = RowId.FromPacked(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, RecordPointerSize)));
        return (id.Row, id.Page);
    }

    /// <summary>Size of a record pointer: the row byte and the 3-byte page.</summary>
    public const int RecordPointerSize = 4;

    /// <summary>Writes a record pointer — the inverse of <see cref="ReadRecordPointer(int)"/>.</summary>
    public static void WriteRecordPointer(Span<byte> buffer, int offset, int row, int page) =>
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset, RecordPointerSize), new RowId(page, row).Packed);

    public ReadOnlySpan<byte> Slice(int offset, int length) => Span.Slice(offset, length);

    public string ReadString(int offset, int length, Encoding encoding) => encoding.GetString(Span.Slice(offset, length));
}