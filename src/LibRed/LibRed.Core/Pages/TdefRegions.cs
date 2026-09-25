using LibRed.Formats;
using System.Buffers.Binary;

namespace LibRed.Pages;

/// <summary>
/// Where each variable-length region of a table definition begins (page-02a §3.3). Only the first has a fixed
/// offset; every later one is found by stepping over all of its predecessors, including the column names,
/// whose lengths come out of the file. Anything reaching into a TDEF it did not itself parse — adding an index
/// or a relationship block, repointing a B-tree root, splitting the definition apart to rewrite it — needs the
/// same walk, so they share this one.
/// </summary>
/// <param name="ColumnCount">Header <c>0x2D</c>: descriptors and names alike.</param>
/// <param name="DataCount">Header <c>0x33</c>: the real index count, sizing both the statistics and the
/// index-data blocks.</param>
/// <param name="LogicalCount">Header <c>0x2F</c>: info blocks and index names.</param>
/// <param name="Stats">Start of the per-index statistics — the one fixed offset, <c>0x3F</c>.</param>
/// <param name="ColumnDescriptors">Start of the 25-byte column descriptors; the names follow them.</param>
/// <param name="DataBlocks">Start of the 52-byte index-data blocks, past the column names.</param>
/// <param name="InfoBlocks">Start of the 28-byte logical index-info blocks.</param>
/// <param name="IndexNames">Start of the index names, and so the end of the info blocks.</param>
internal readonly record struct TdefRegions(
    int ColumnCount,
    int DataCount,
    int LogicalCount,
    int Stats,
    int ColumnDescriptors,
    int DataBlocks,
    int InfoBlocks,
    int IndexNames)
{
    /// <summary>Walks <paramref name="tdef"/> — the whole definition, continuation pages already stitched in,
    /// since the regions run straight across a page boundary.</summary>
    /// <remarks>
    /// Every region is bounded as it is crossed. The counts and name lengths are all file-sourced, and
    /// unchecked they carry the walk past the buffer — or, when a multiply overflows, back inside it at the
    /// wrong place, which lands a write on some other index's block with no error raised at all.
    /// </remarks>
    public static TdefRegions Of(ReadOnlySpan<byte> tdef, JetFormatBase format)
    {
        int columnCount = BinaryPrimitives.ReadUInt16LittleEndian(tdef.Slice(format.TdefColumnCountOffset, 2));
        int dataCount = BinaryPrimitives.ReadInt32LittleEndian(tdef.Slice(format.TdefIndexCountOffset, 4));
        int logicalCount = BinaryPrimitives.ReadInt32LittleEndian(tdef.Slice(format.TdefLogicalIndexCountOffset, 4));

        int stats = format.TdefRealIndexBlockOffset;
        int descriptors = TableDefinitionPage.CheckedRegionEnd(
            stats, dataCount, format.RealIndexEntrySize, tdef.Length, "index statistics");
        int pos = TableDefinitionPage.CheckedRegionEnd(
            descriptors, columnCount, format.ColumnDescriptorSize, tdef.Length, "column descriptors");

        for (int i = 0; i < columnCount; i++)
        {
            int text = TableDefinitionPage.CheckedRegionEnd(pos, 1, 2, tdef.Length, "a column-name length");
            pos = TableDefinitionPage.CheckedRegionEnd(
                text, 1, BinaryPrimitives.ReadUInt16LittleEndian(tdef.Slice(pos, 2)), tdef.Length, "a column name");
        }

        int dataBlocks = pos;
        int infoBlocks = TableDefinitionPage.CheckedRegionEnd(
            dataBlocks, dataCount, IndexBlockFormat.DataBlockSize, tdef.Length, "index-data blocks");
        int indexNames = TableDefinitionPage.CheckedRegionEnd(
            infoBlocks, logicalCount, IndexBlockFormat.InfoBlockSize, tdef.Length, "logical-index blocks");

        return new TdefRegions(
            columnCount, dataCount, logicalCount, stats, descriptors, dataBlocks, infoBlocks, indexNames);
    }
}
