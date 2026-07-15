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

    /// <summary>The "variable-table index" stored at descriptor offset 7 (<see cref="JetFormatBase.ColumnVariableIndexOffset"/>):
    /// the count of variable-length columns whose column-id is smaller than this one's. For a variable column this
    /// equals <see cref="VariableIndex"/>; for a fixed column it is the running count of preceding variable columns
    /// (Access stores it on fixed columns too, and its strict row reader relies on it). -1 = unset (falls back to
    /// the legacy "0 for fixed" behaviour used by the ADD COLUMN path).</summary>
    public int VariableTableIndex { get; init; } = -1;

    public bool IsFixedLength { get; init; }

    /// <summary>Whether the column accepts NULL. False for a NOT NULL / Required column — read from the
    /// <c>Required</c> property in the extended-properties (<c>LvProp</c>) blob, set by the catalog after
    /// the descriptors are decoded (like <see cref="DefaultValue"/>, the flag lives outside the TDEF).</summary>
    public bool IsNullable { get; internal set; } = true;

    public bool IsAutoNumber { get; init; }

    /// <summary>The column's <c>updatable</c> flag bit (0x0F bit 0x02) — set on essentially every column.
    /// Modelled so it round-trips explicitly rather than riding through the raw descriptor.</summary>
    public bool IsUpdatable { get; init; } = true;

    /// <summary>An AutoNumber column that generates GUIDs (Replication ID), not sequential Longs — flag bit
    /// 0x0F/0x40. Modelled for faithful round-trip; LibRed does not create these but preserves them.</summary>
    public bool IsGuidAutoNumber { get; init; }

    /// <summary>A hyperlink column (a Memo presented as a hyperlink) — flag bit 0x0F/0x80.</summary>
    public bool IsHyperlink { get; init; }

    /// <summary>The column can store compressed Unicode text (§7) — extended-flag bit 0x10/0x01.</summary>
    public bool SupportsCompressedUnicode { get; init; }

    /// <summary>A calculated (computed) column (ACE 14+) — extended-flag bits 0x10/0xC0. LibRed reads and
    /// preserves the flag; evaluating a calculated column's expression is separate (see the format spec).</summary>
    public bool IsCalculated { get; init; }

    /// <summary>AutoNumber (COUNTER) seed and increment. The increment is read from the TDEF header (0x18);
    /// the seed is the header's last-assigned value (0x14) plus the increment — which equals the original
    /// seed on a freshly created table (before any inserts advance the last value). Default 1/1.</summary>
    public int Seed { get; internal set; } = 1;
    public int Increment { get; internal set; } = 1;

    /// <summary>The column's <c>DefaultValue</c> property (an expression's source text, e.g. <c>"0"</c>
    /// or <c>"'hi'"</c>), read from the table's extended-properties (<c>LvProp</c>) blob; null if none.
    /// Set by the catalog after the descriptors are decoded (the property lives outside the TDEF).</summary>
    public string? DefaultValue { get; internal set; }

    /// <summary>A "Random" AutoNumber — an AutoNumber column whose <see cref="DefaultValue"/> is the built-in
    /// <c>GenUniqueID()</c> expression (Access's "New Values = Random"). Such a column is assigned a random
    /// Int32 on insert instead of the sequential seed/increment counter, and its TDEF high-water is left
    /// unadvanced. Verified byte-identical to a UI-authored Random AutoNumber (see the format spec).</summary>
    public bool IsRandomAutoNumber => IsAutoNumber && DefaultValue is not null
        && DefaultValue.Trim().Equals("GenUniqueID()", StringComparison.OrdinalIgnoreCase);

    /// <summary>Precision/scale for <see cref="JetDataType.FixedPoint"/> columns.</summary>
    public byte Precision { get; init; }
    public byte Scale { get; init; }

    /// <summary>The text collation (LCID + sort-order version) for a non-numeric column — read from the
    /// descriptor's locale bytes (<c>0x0B/0x0C</c>) and version byte (<c>0x0D</c>). Numeric columns reuse
    /// those bytes for precision/scale and carry no collation. Defaults to General legacy, which is what
    /// every file LibRed currently handles uses, and the only order whose index keys it can encode.</summary>
    public Collation Collation { get; init; } = Collation.GeneralLegacy;

    /// <summary>The column's original on-disk descriptor bytes (the 25-byte Jet4 record), captured verbatim on
    /// read. Every documented field is now modelled explicitly, so this is carried only to re-emit the
    /// genuinely <b>reserved/unknown</b> bytes on a rewrite — the reserved words at <c>0x03</c> and <c>0x11</c>,
    /// and the undocumented bits of the two flag bytes (<c>0x0F</c>/<c>0x10</c>) — instead of stamping zero over
    /// them (the faithful round-trip rule). Null for a freshly-built (never-read) column.</summary>
    public byte[]? RawDescriptor { get; init; }

    /// <summary>Undocumented flag bits (byte 0x0F) to force-set — the system-catalog column marker (0x10) and
    /// security-identifier marker (0x20) Access sets on MSys* columns. 0 for ordinary columns.</summary>
    public byte SystemFlags { get; init; }
}
