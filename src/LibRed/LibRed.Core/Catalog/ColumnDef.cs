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

    /// <summary>For fixed-length columns, the offset of the value within the row's fixed-data region.</summary>
    public int FixedOffset { get; init; }

    /// <summary>Position of this column among the variable-length columns (in column-id order); -1 if fixed.</summary>
    public int VariableIndex { get; init; } = -1;

    public bool IsFixedLength { get; init; }

    /// <summary>Whether the column accepts NULL. False for a NOT NULL / Required column — read from the
    /// <c>Required</c> property in the extended-properties (<c>LvProp</c>) blob, set by the catalog after
    /// the descriptors are decoded (like <see cref="DefaultValue"/>, the flag lives outside the TDEF).</summary>
    public bool IsNullable { get; internal set; } = true;

    public bool IsAutoNumber { get; init; }

    /// <summary>AutoNumber (COUNTER) seed and increment. The increment is read from the TDEF header (0x18);
    /// the seed is the header's last-assigned value (0x14) plus the increment — which equals the original
    /// seed on a freshly created table (before any inserts advance the last value). Default 1/1.</summary>
    public int Seed { get; internal set; } = 1;
    public int Increment { get; internal set; } = 1;

    /// <summary>The column's <c>DefaultValue</c> property (an expression's source text, e.g. <c>"0"</c>
    /// or <c>"'hi'"</c>), read from the table's extended-properties (<c>LvProp</c>) blob; null if none.
    /// Set by the catalog after the descriptors are decoded (the property lives outside the TDEF).</summary>
    public string? DefaultValue { get; internal set; }

    /// <summary>Precision/scale for <see cref="JetDataType.FixedPoint"/> columns.</summary>
    public byte Precision { get; init; }
    public byte Scale { get; init; }
}
