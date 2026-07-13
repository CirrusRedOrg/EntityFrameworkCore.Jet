namespace LibRed.Formats;

/// <summary>
/// The flag bits at offset <see cref="IndexBlockFormat.FlagsOffset"/> of an index-data block. Shared by the
/// read side (<c>TableDefinitionPage</c>) and the write sides (<c>TdefBuilder</c>, <c>TableCreator</c>) so the
/// bit values can't drift. Verified against ACE: plain 0x0080, IGNORE NULL 0x0082, DISALLOW NULL 0x0088,
/// primary key 0x0089.
/// </summary>
internal static class IndexFlags
{
    /// <summary>The index is unique (WITH PRIMARY / a unique index or constraint).</summary>
    public const ushort Unique = 0x0001;

    /// <summary>WITH IGNORE NULL: rows with a null in any indexed column are excluded from the index.</summary>
    public const ushort IgnoreNulls = 0x0002;

    /// <summary>WITH DISALLOW NULL (Required): the index rejects a null key. Set for primary keys and for
    /// any required index.</summary>
    public const ushort Required = 0x0008;

    /// <summary>Always set on Access 2000+ index-data blocks.</summary>
    public const ushort AlwaysSet = 0x0080;
}
