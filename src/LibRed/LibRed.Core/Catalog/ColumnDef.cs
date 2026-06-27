namespace LibRed.Catalog;

/// <summary>
/// Describes a single column of a table: its name, type, physical layout and flags.
/// Decoded from the column descriptors in a <see cref="Pages.TableDefinitionPage"/>.
/// </summary>
public sealed class ColumnDef
{
    public required string Name { get; init; }
    public required JetDataType Type { get; init; }

    /// <summary>Zero-based logical column index (declaration order).</summary>
    public int Index { get; init; }

    /// <summary>Physical offset/ordinal used to locate the value within a row record.</summary>
    public int ColumnId { get; init; }

    /// <summary>Declared length in bytes for fixed-width/text columns.</summary>
    public int Length { get; init; }

    public bool IsFixedLength { get; init; }
    public bool IsNullable { get; init; } = true;
    public bool IsAutoNumber { get; init; }

    /// <summary>Precision/scale for <see cref="JetDataType.FixedPoint"/> columns.</summary>
    public byte Precision { get; init; }
    public byte Scale { get; init; }
}
