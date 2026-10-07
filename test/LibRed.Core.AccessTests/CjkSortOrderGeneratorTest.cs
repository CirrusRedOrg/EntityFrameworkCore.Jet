using System.IO.Compression;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// GENERATOR: the Chinese, Japanese and Korean sort-order resources, one per order.
//
// For each order, ACE encodes every BMP character in an indexed column of the Access-authored fixture the order
// is named after, and each key is compared with what LibRed's General encoder of the same version gives — with
// the CJK tables suppressed, but Korean's lead-byte move left on, since that is a rule rather than data. Every
// character where they differ goes into the order's table as the one weight ACE stored for it: its primary
// bytes, and its secondary.
//
// That every departure IS one weight is a measured claim, and the generator enforces it rather than assuming
// it: a key with two weights, a kana section or an inline record is a mechanism the encoder would have to
// implement, not data a table can carry, so it is reported and the run fails.
//
// Opt-in via LIBRED_GENERATE_CJK=1: each order inserts ~63,000 rows through ACE and rewrites a checked-in binary.
[Collection(AceCollection.Name)]
public class CjkSortOrderGeneratorTest(ITestOutputHelper output)
{
    private const string ResourceDirectory = "src/LibRed/LibRed.Core/Resources/Cjk";

    public static TheoryData<string> Orders => [.. JetCjkSortOrders.Orders.Select(o => o.Name)];

    [Theory]
    [MemberData(nameof(Orders))]
    public void Generate_the_sort_order_resource(string name)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_GENERATE_CJK") == "1",
            "set LIBRED_GENERATE_CJK=1 — this measures ~63,000 characters through ACE and rewrites a resource");

        // Suppressed before anything can touch a CJK collation — even formatting one reaches the tailoring, via
        // IsIndexKeyEncodable — so the generator never needs the resource it is about to write, and bootstraps
        // from a stale or absent one.
        JetCjkSortOrders.Suppressed = true;
        Collation collation = JetCjkSortOrders.Orders.Single(o => o.Name == name).Collation;
        string fixture = TestDatabases.Data($"{name}.accdb");
        var entries = new SortedDictionary<int, (byte[] Primary, byte Secondary)>();
        var leftover = new SortedDictionary<int, (string Ace, string General)>();
        int agreed = 0, refused = 0;
        try
        {
            using (var db = JetDatabase.Open(fixture))
                Assert.True(db.Collation == collation, $"{name}.accdb carries {db.Collation}, not {collation}.");
            var column = new ColumnDef { Name = "K", Type = JetDataType.Text, Index = 0, Collation = collation };

            for (int chunk = 0x0000; chunk <= 0xF000; chunk += 0x1000)
            {
                string[] characters = Range(chunk, chunk + 0x0FFF);
                Dictionary<string, string> ace = AceKeys(fixture, characters);
                foreach (string text in characters)
                {
                    if (!ace.TryGetValue(text, out string? key)) { refused++; continue; }
                    string ours;
                    try { ours = Convert.ToHexString(IndexKeyCodec.Encode([(column, true)], [text])); }
                    catch (NotSupportedException) { ours = "(refused)"; }
                    if (ours == key) { agreed++; continue; }
                    if (TryReadOneWeight(key, out byte[] primary, out byte secondary))
                        entries[text[0]] = (primary, secondary);
                    else
                        leftover[text[0]] = (key, ours);
                }
            }
        }
        finally { JetCjkSortOrders.Suppressed = false; }

        output.WriteLine($"{name} (0x{collation.Lcid:X8} v{collation.Version}): {agreed} agree with General, " +
                         $"{entries.Count} tabulated, " +
                         $"{leftover.Count} left over, {refused} ACE would not store");
        foreach ((int codePoint, (string ace, string general)) in leftover.Take(40))
            output.WriteLine($"  U+{codePoint:X4}  ACE {ace,-40} General {general}");

        byte[] blob = Build(entries);
        string directory = Path.Combine(RepositoryRoot(), ResourceDirectory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{name}.bin");
        File.WriteAllBytes(path, blob);
        output.WriteLine($"wrote {path} ({blob.Length:N0} bytes)");

        SortedDictionary<int, (byte[] Primary, byte Secondary)> reloaded = Parse(blob);
        Assert.Equal(entries.Keys, reloaded.Keys);
        foreach ((int codePoint, (byte[] primary, byte secondary)) in entries)
        {
            Assert.Equal(primary, reloaded[codePoint].Primary);
            Assert.Equal(secondary, reloaded[codePoint].Secondary);
        }
        Assert.Empty(leftover);
    }

    /// <summary>
    /// Reads a key holding exactly one weight — <c>7F primary 01 00</c>, or <c>7F primary 01 secondary 00</c>
    /// — into its primary bytes and its secondary, the default <c>02</c> when the section is empty.
    /// </summary>
    private static bool TryReadOneWeight(string hex, out byte[] primary, out byte secondary)
    {
        byte[] key = Convert.FromHexString(hex);
        primary = [];
        secondary = 0x02;
        if (key.Length < 4 || key[0] != 0x7F || key[^1] != 0x00) return false;
        if (key[^2] == 0x01)
        {
            primary = key[1..^2];
        }
        else if (key.Length >= 5 && key[^3] == 0x01)
        {
            primary = key[1..^3];
            secondary = key[^2];
        }
        else return false;
        return primary.Length > 0;
    }

    /// <summary>A count, then four zlib streams: code-point deltas as base-128 integers, primary lengths,
    /// primary bytes, and one secondary per entry — what <c>JetCjkSortOrders</c> reads.</summary>
    private static byte[] Build(SortedDictionary<int, (byte[] Primary, byte Secondary)> entries)
    {
        var deltas = new List<byte>();
        int previous = 0;
        foreach (int codePoint in entries.Keys)
        {
            WriteVarInt(deltas, codePoint - previous);
            previous = codePoint;
        }
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write(entries.Count);
        WriteStream(writer, [.. deltas]);
        WriteStream(writer, [.. entries.Values.Select(e => (byte)e.Primary.Length)]);
        WriteStream(writer, [.. entries.Values.SelectMany(e => e.Primary)]);
        WriteStream(writer, [.. entries.Values.Select(e => e.Secondary)]);
        writer.Flush();
        return stream.ToArray();
    }

    private static SortedDictionary<int, (byte[] Primary, byte Secondary)> Parse(byte[] blob)
    {
        var reader = new BinaryReader(new MemoryStream(blob));
        int count = reader.ReadInt32();
        byte[] deltas = ReadStream(reader), lengths = ReadStream(reader), primaries = ReadStream(reader),
            secondaries = ReadStream(reader);
        var entries = new SortedDictionary<int, (byte[] Primary, byte Secondary)>(Comparer<int>.Default);
        int codePoint = 0, cursor = 0, offset = 0;
        for (int i = 0; i < count; i++)
        {
            codePoint += ReadVarInt(deltas, ref cursor);
            entries[codePoint] = (primaries[offset..(offset + lengths[i])], secondaries[i]);
            offset += lengths[i];
        }
        return entries;
    }

    private static void WriteStream(BinaryWriter writer, byte[] data)
    {
        var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(data, 0, data.Length);
        writer.Write((int)compressed.Length);
        writer.Write(compressed.ToArray());
    }

    private static byte[] ReadStream(BinaryReader reader)
    {
        byte[] compressed = reader.ReadBytes(reader.ReadInt32());
        var output = new MemoryStream();
        using (var inflate = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress))
            inflate.CopyTo(output);
        return output.ToArray();
    }

    private static void WriteVarInt(List<byte> target, int value)
    {
        while (value >= 0x80) { target.Add((byte)((value & 0x7F) | 0x80)); value >>= 7; }
        target.Add((byte)value);
    }

    private static int ReadVarInt(byte[] source, ref int offset)
    {
        int value = 0, shift = 0;
        while (true)
        {
            byte b = source[offset++];
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
        }
    }

    private static string[] Range(int first, int last)
    {
        var characters = new List<string>();
        for (int c = first; c <= last; c++)
            if (!char.IsControl((char)c) && !char.IsSurrogate((char)c))
                characters.Add(((char)c).ToString());
        return [.. characters];
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EFCore.Jet.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("EFCore.Jet.sln not found above the test output.");
    }

    private static Dictionary<string, string> AceKeys(string source, string[] samples)
    {
        string path = TemporaryDatabase.CopyPath(source, "cjkgen-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE Gen (K TEXT(50), V LONG)");
                Exec(connection, "CREATE INDEX IX_Gen ON Gen (K)");
                for (int i = 0; i < samples.Length; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO Gen (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", samples[i]);
                    insert.Parameters.AddWithValue("v", i);
                    try { insert.ExecuteNonQuery(); } catch (Exception) { /* ACE refused this value */ }
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("Gen");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_Gen");
            ColumnDef keyColumn = table.Definition.FindColumn("K")!;
            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);
            var keys = new Dictionary<string, string>();
            foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
                if (rows.TryGetValue(rowId, out object?[]? values) && values[keyColumn.Index] is string text)
                    keys[text] = Convert.ToHexString(stored);
            return keys;
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Exec(System.Data.OleDb.OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}