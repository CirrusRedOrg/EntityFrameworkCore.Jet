namespace LibRed.Formats;

/// <summary>A relationship's direction as an index-info block records it
/// (<see cref="JetFormatBase.IndexInfoFkTypeOffset"/>).</summary>
public enum ForeignKeyType : byte
{
    /// <summary>Not a relationship's index.</summary>
    None = 0x00,

    /// <summary>This table is the parent, the referenced end — the half Access names <c>.r…</c> and keeps out of
    /// its schema views.</summary>
    Incoming = 0x01,

    /// <summary>This table is the child, the end holding the foreign key, with an index.</summary>
    Outgoing = 0x02,

    /// <summary>The child end of a relationship declared <c>FOREIGN KEY NO INDEX</c>.</summary>
    OutgoingNoIndex = 0x03,
}