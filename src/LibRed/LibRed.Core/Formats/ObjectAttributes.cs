namespace LibRed.Formats;

/// <summary>
/// The <c>MSysObjects.Flags</c> bits that decide how an object is classified — the way Access classifies it, not
/// by name (measured on ACE 16; docs/format/system-catalog.md).
/// </summary>
[Flags]
public enum ObjectAttributes : uint
{
    None = 0,

    /// <summary>The system attribute, beside <see cref="System"/> on the engine's own objects.</summary>
    SystemAttribute = 0x00000002,

    /// <summary>A hidden object — Access's application tables, the nav-pane group, <c>MSysResources</c>, and
    /// EFCore.Jet's <c>#Dual</c> helper. Without <see cref="System"/> it is an <c>ACCESS TABLE</c> in the schema
    /// rowsets, and Access keeps it out of its user-table list.</summary>
    Hidden = 0x00000008,

    /// <summary>A complex column's flat storage table (<c>MSysComplexType_*</c>): not listed in the schema
    /// rowsets at all.</summary>
    ComplexStorage = 0x00030000,

    /// <summary>A complex column's flat table, <c>f_&lt;GUID&gt;_&lt;column&gt;</c>, with <see cref="System"/>.</summary>
    ComplexFlatTable = 0x000A0000,

    /// <summary>A table that owns a complex column — set on exactly those tables, and on no table without one
    /// (measured across Access-written files; system-catalog §11).</summary>
    OwnsComplexColumns = 0x00040000,

    /// <summary>Set on every stored query, whose kind fills the low byte (<see cref="QueryDefType"/>), and on DAO's
    /// <c>SingleRecord</c> pseudo-object.</summary>
    QueryDef = 0x10000000,

    /// <summary>A system object: a <c>SYSTEM TABLE</c> in the schema rowsets.</summary>
    System = 0x80000000,
}