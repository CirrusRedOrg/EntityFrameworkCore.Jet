namespace LibRed.Formats;

/// <summary>A usage-map record's first byte: which of the two forms it takes (§9).</summary>
internal enum UsageMapType : byte
{
    /// <summary>A start page, then a bitmap whose bit <c>i</c> stands for page <c>start + i</c>.</summary>
    Inline = 0x00,

    /// <summary>Pointers to dedicated bitmap pages (type 0x0105), each covering a fixed range of pages.</summary>
    Reference = 0x01,
}