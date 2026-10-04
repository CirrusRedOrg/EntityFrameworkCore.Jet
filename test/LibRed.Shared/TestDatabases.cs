using Xunit;

namespace LibRed.Core.Tests;

/// <summary>Paths to the real database files copied alongside the test assembly, and what reads and writes them
/// directly.</summary>
internal static class TestDatabases
{
    /// <summary>The path to a checked-in fixture by file name — every <c>Data\*.accdb</c> is copied
    /// alongside the test assembly. Use this for the sort-order fixtures, one per Access "New database sort
    /// order" entry, rather than adding a property each.</summary>
    public static string Data(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Data", fileName);

    /// <summary>The format of the database at <paramref name="path"/>, read from its own page 0 as an open
    /// reads it — never assumed from what the fixture is believed to be.</summary>
    public static Formats.JetFormatBase FormatOf(string path)
    {
        // Shared for writing too: a test may ask while its own connection still holds the file open.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Formats.JetFormatBase.Detect(stream);
    }

    // Raw page I/O on a closed file, for a test that stands in for another writer — planting released pages, moving or
    // breaking page 0's map pointers. A writable channel cannot do it: its close merges the released map and checks
    // those very pointers, undoing or refusing the state being planted.

    /// <summary>Page <paramref name="page"/> of the file at <paramref name="path"/>, straight off disk.</summary>
    public static byte[] ReadPage(string path, int page)
    {
        int pageSize = FormatOf(path).PageSize;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[pageSize];
        stream.Position = (long)page * pageSize;
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>Overwrites page <paramref name="page"/> of the file at <paramref name="path"/>.</summary>
    public static void WritePage(string path, int page, byte[] bytes)
    {
        int pageSize = FormatOf(path).PageSize;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        stream.Position = (long)page * pageSize;
        stream.Write(bytes);
    }

    /// <summary>Adds <paramref name="bytes"/> as a new page at the end of the file at <paramref name="path"/>.</summary>
    public static void AppendPage(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write);
        stream.Write(bytes);
    }

    /// <summary>Points page 0's global map pointer at <paramref name="pointerOffset"/> at
    /// <paramref name="page"/>:<paramref name="row"/>, under the header mask.</summary>
    public static void WriteMapPointer(string path, int pointerOffset, int row, int page)
    {
        Formats.JetFormatBase format = FormatOf(path);
        byte[] page0 = ReadPage(path, 0);
        var pointer = new byte[IO.PageBuffer.RecordPointerSize];
        IO.PageBuffer.WriteRecordPointer(pointer, 0, row, page);
        Pages.DatabaseDefinitionPage.WriteMasked(page0, pointerOffset, pointer, format);
        WritePage(path, 0, page0);
    }

    /// <summary>Whether <paramref name="page"/>'s bit is set in the inline usage map at <paramref name="row"/> of a
    /// holder page's bytes.</summary>
    public static bool MapBit(byte[] holder, Formats.JetFormatBase format, int row, int page) =>
        Storage.BitmapBits.Get(InlineMapBits(holder, format, row, page, out int bit), bit);

    /// <summary>Sets or clears <paramref name="page"/>'s bit in the inline usage map at <paramref name="row"/> of a
    /// holder page's bytes.</summary>
    public static void SetMapBit(byte[] holder, Formats.JetFormatBase format, int row, int page, bool set) =>
        Storage.BitmapBits.Set(InlineMapBits(holder, format, row, page, out int bit), bit, set);

    private static Span<byte> InlineMapBits(byte[] holder, Formats.JetFormatBase format, int row, int page, out int bit)
    {
        var parsed = new Pages.DataPage();
        parsed.Read(new IO.PageBuffer(holder, 0), format);
        Pages.DataPage.RowSlot slot = parsed.Rows[row];
        Span<byte> record = holder.AsSpan(slot.Offset, slot.Length);
        Assert.Equal(Formats.UsageMapType.Inline, Storage.UsageMap.RecordType(record));
        bit = page - Storage.UsageMap.StartPage(record, format);
        Span<byte> bits = Storage.UsageMap.InlineBits(record, format);
        Assert.InRange(bit, 0, bits.Length * 8 - 1);
        return bits;
    }

    /// <summary>The global usage map page 0's pointer at <paramref name="pointerOffset"/> names, found as the allocator
    /// finds it, wherever it is: the pointer, the holder page's bytes, and the record's slot on it.</summary>
    public static (int Row, int Page, byte[] Holder, Pages.DataPage.RowSlot Slot) GlobalMap(IO.PageChannel channel, int pointerOffset)
    {
        (int row, int page) = Pages.DatabaseDefinitionPage.ReadMapPointer(channel.ReadPage(0).Span, pointerOffset, channel.Format);
        (IO.PageBuffer holder, _, Pages.DataPage.RowSlot slot) = Storage.UsageMap.ReadRecord(channel, row, page, "Global map pointer");
        return (row, page, holder.Span.ToArray(), slot);
    }

    /// <summary>An Access 2007 (ACE 12 / ACCDB) Northwind sample.</summary>
    public static string NorthwindAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    /// <summary>A 200-column ACCDB whose table definition spans multiple TDEF pages.</summary>
    public static string WideTableAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "WideTable.accdb");

    /// <summary>An ACCDB with Decimal/Numeric columns and known values.</summary>
    public static string DecimalsAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "Decimals.accdb");

    /// <summary>An <b>ACE 17</b> (version byte <c>0x06</c>, Access 2019+) ACCDB using BIGINT and DATETIME2.
    /// The name predates the distinction and is misleading: <c>Version16_2016</c> is <c>0x05</c>, and only
    /// DATETIME2 needs <c>0x06</c>. An ACE 2016 install — which is what CI has — cannot open this file at
    /// all, so use it only for tests that are genuinely about the 0x06 format, and guard those with
    /// <see cref="Tests.Shared.AceTestDatabase.SupportsColumnType"/>. For anything else, create a database at
    /// the lowest version the case needs.</summary>
    public static string Ace16TypesAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "Ace16Types.accdb");

    /// <summary>EF Core's BuiltInDataTypes database — broad coverage of every mapped column type.</summary>
    public static string BuiltInDataTypesAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "BuiltInDataTypes.accdb");

    /// <summary>EF Core's EverythingIsBytes database — every entity has a byte[] primary key (3/4/5/8/16-byte
    /// values), for byte-faithful binary index-key checks.</summary>
    public static string EverythingIsBytesAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "EverythingIsBytes.accdb");

    /// <summary>An Access-authored ACCDB using the Spanish Traditional sort order, where "ch" and "ll" are
    /// single letters sorting after "c" and "l".</summary>
    public static string SpanishTraditionalAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "SpanishTraditional.accdb");

    /// <summary>An Access-authored ACCDB using the Spanish Modern sort order, which sorts "ch" and "ll" as
    /// the plain letter pairs. Differs from <see cref="SpanishTraditionalAccdb"/> in that alone.</summary>
    public static string SpanishModernAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "SpanishModern.accdb");

    /// <summary>A password-encrypted ACCDB (Office Agile encryption; the password is "Test").</summary>
    public static string EncryptedAccdb { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "EncryptedTest.accdb");

    /// <summary>The password for <see cref="EncryptedAccdb"/>.</summary>
    public const string EncryptedPassword = "Test";
}