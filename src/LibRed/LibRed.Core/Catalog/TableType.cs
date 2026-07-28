namespace LibRed.Catalog;

/// <summary>The table-type marker in a TDEF page (byte at the format's table-type offset).</summary>
public enum TableType : byte
{
    /// <summary>A user table ('N').</summary>
    User = 0x4E,

    /// <summary>A system (MSys*) table ('S').</summary>
    System = 0x53,
}
