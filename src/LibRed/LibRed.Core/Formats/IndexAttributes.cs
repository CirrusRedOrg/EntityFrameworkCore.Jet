namespace LibRed.Formats;

/// <summary>
/// The flag bits at <see cref="JetFormatBase.IndexDataFlagsOffset"/> of an index-data block. Verified against
/// ACE: plain 0x0080, IGNORE NULL 0x0082, DISALLOW NULL 0x0088, primary key 0x0089.
/// </summary>
[Flags]
public enum IndexAttributes : ushort
{
    None = 0,

    /// <summary>The index is unique (WITH PRIMARY / a unique index or constraint).</summary>
    Unique = 0x0001,

    /// <summary>WITH IGNORE NULL: rows with a null in any indexed column are excluded from the index.</summary>
    IgnoreNulls = 0x0002,

    /// <summary>WITH DISALLOW NULL (Required): the index rejects a null key. Set for primary keys and for
    /// any required index.</summary>
    Required = 0x0008,

    /// <summary>Always set on Access 2000+ index-data blocks.</summary>
    AlwaysSet = 0x0080,

    /// <summary>The index is over a complex (multi-value / attachment) column. Set on every such index Access
    /// writes — always as <c>0x0289</c>, with unique, required and always-set — and on no other (measured across
    /// Access-written files).</summary>
    ComplexColumn = 0x0200,
}