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
        if (countSize != 2 || row.Length < countSize)
            throw new InvalidDataException("Row is too short to contain its 2-byte column count.");

        _row = row;
        _countSize = countSize;
        ColumnCount = BinaryPrimitives.ReadUInt16LittleEndian(row[..countSize]);
        NullBitmapSize = (ColumnCount + 7) / 8;
        if (row.Length < countSize + NullBitmapSize)
            throw new InvalidDataException(
                $"Row is {row.Length} bytes, too short for its {NullBitmapSize}-byte null bitmap.");

        if (!hasVar)
        {
            NumVar = 0;
            VarTableStart = -1;
            FixedRegionLength = row.Length - countSize - NullBitmapSize;
            return;
        }

        int numVarPos = row.Length - NullBitmapSize - 2;
        if (numVarPos < countSize + 2)
            throw new InvalidDataException("Row is too short to contain a variable-column trailer.");
        NumVar = BinaryPrimitives.ReadUInt16LittleEndian(row.Slice(numVarPos, 2));
        long tableStart = (long)numVarPos - ((long)NumVar + 1) * 2;
        if (tableStart < countSize || tableStart > numVarPos)
            throw new InvalidDataException(
                $"Row declares {NumVar} variable slots, placing its offset table outside the row.");
        VarTableStart = (int)tableStart;

        int previous = VarOffset(0);
        if (previous < countSize || previous > VarTableStart)
            throw new InvalidDataException(
                $"Row variable-data end {previous} is outside the data region ending at {VarTableStart}.");
        for (int entry = 1; entry <= NumVar; entry++)
        {
            int current = VarOffset(entry);
            if (current < countSize || current > previous)
                throw new InvalidDataException(
                    $"Row variable offset {entry} ({current}) is outside or above its preceding boundary {previous}.");
            previous = current;
        }
        // The last offset-table entry is the variable-data start (= count field + fixed region).
        FixedRegionLength = previous - countSize;
    }

    /// <summary>Parses <paramref name="row"/>; <paramref name="hasVar"/> is whether the schema has any variable column.</summary>
    public static RowLayout Parse(ReadOnlySpan<byte> row, int countSize, bool hasVar) => new(row, countSize, hasVar);

    /// <summary>The raw bytes of variable column <paramref name="variableIndex"/> (end-first offset table).</summary>
    public ReadOnlySpan<byte> VarChunk(int variableIndex)
    {
        if (variableIndex < 0 || variableIndex >= NumVar)
            throw new InvalidDataException(
                $"Row has {NumVar} variable slots but column metadata requests slot {variableIndex}.");
        int start = VarOffset(NumVar - variableIndex);
        int end = VarOffset(NumVar - variableIndex - 1);
        return _row[start..end];
    }

    private int VarOffset(int entry) =>
        BinaryPrimitives.ReadUInt16LittleEndian(_row.Slice(VarTableStart + entry * 2, 2));
}
