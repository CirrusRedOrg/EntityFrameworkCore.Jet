using System.Buffers.Binary;

namespace LibRed.Storage;

/// <summary>
/// Parses the structural trailer of an inline row record once (spec §5), so the several call sites that
/// need to locate a row's regions don't each re-derive the offset arithmetic. Layout:
/// <code>
/// [count:2] [fixed data] [var data] [varOffsetTable:(numVar+1)x2] [numVar:2] [nullBitmap]
/// </code>
/// The leading count is <c>maxColumnId + 1</c> and drives the null-bitmap width. A table with NO variable
/// columns omits the whole variable section (offset table + numVar) — such a row can't self-describe that,
/// so the caller passes <paramref name="hasVar"/> from the schema.
/// </summary>
internal readonly ref struct RowLayout
{
    private readonly ReadOnlySpan<byte> _row;
    private readonly int _countSize;

    /// <summary>The leading column count (= max column id + 1).</summary>
    public int ColumnCount { get; }
    /// <summary>Null-bitmap width in bytes, from the leading count.</summary>
    public int NullBitmapSize { get; }
    /// <summary>Number of variable columns stored (0 when the table has no variable section).</summary>
    public int NumVar { get; }
    /// <summary>Offset of the variable-offset table, or -1 when there is no variable section.</summary>
    public int VarTableStart { get; }
    /// <summary>Length of the fixed-data region (bytes between the leading count and the variable data).</summary>
    public int FixedRegionLength { get; }

    private RowLayout(ReadOnlySpan<byte> row, int countSize, bool hasVar)
    {
        _row = row;
        _countSize = countSize;
        ColumnCount = BinaryPrimitives.ReadUInt16LittleEndian(row[..countSize]);
        NullBitmapSize = (ColumnCount + 7) / 8;

        if (!hasVar)
        {
            NumVar = 0;
            VarTableStart = -1;
            FixedRegionLength = row.Length - countSize - NullBitmapSize;
            return;
        }

        int numVarPos = row.Length - NullBitmapSize - 2;
        NumVar = BinaryPrimitives.ReadUInt16LittleEndian(row.Slice(numVarPos, 2));
        VarTableStart = numVarPos - (NumVar + 1) * 2;
        // The last offset-table entry is the variable-data start (= count field + fixed region).
        FixedRegionLength = BinaryPrimitives.ReadUInt16LittleEndian(row.Slice(VarTableStart + NumVar * 2, 2)) - countSize;
    }

    /// <summary>Parses <paramref name="row"/>; <paramref name="hasVar"/> is whether the schema has any variable column.</summary>
    public static RowLayout Parse(ReadOnlySpan<byte> row, int countSize, bool hasVar) => new(row, countSize, hasVar);

    /// <summary>The raw bytes of variable column <paramref name="variableIndex"/> (end-first offset table).</summary>
    public ReadOnlySpan<byte> VarChunk(int variableIndex)
    {
        int start = VarOffset(NumVar - variableIndex);
        int end = VarOffset(NumVar - variableIndex - 1);
        return _row[start..end];
    }

    private int VarOffset(int entry) =>
        BinaryPrimitives.ReadUInt16LittleEndian(_row.Slice(VarTableStart + entry * 2, 2));
}
