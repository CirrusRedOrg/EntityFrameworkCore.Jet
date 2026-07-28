using System.Buffers.Binary;
using System.Text;

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

    /// <summary>Reads a 3-byte little-endian page pointer (used by index/usage structures).</summary>
    public int ReadInt24(int offset) => Span[offset] | (Span[offset + 1] << 8) | (Span[offset + 2] << 16);

    public ReadOnlySpan<byte> Slice(int offset, int length) => Span.Slice(offset, length);

    public string ReadString(int offset, int length, Encoding encoding) => encoding.GetString(Span.Slice(offset, length));
}
