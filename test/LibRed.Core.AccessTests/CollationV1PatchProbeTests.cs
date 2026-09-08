using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: the only way to put a VERSION-1 collation in front of ACE.
//
// Version 0 could be surveyed because DAO's CreateDatabase takes a raw LANGID, so ACE could be made to
// author a file in any order and its keys read back. Nothing does that for version 1: DAO writes v0 for
// every LANGID it accepts, `Field.CollatingOrder` is read-only throughout the DAO object model, and Access
// honours "New database sort order" only through its own UI. LibRed can create a v1 file but only in a
// collation it can already encode — creating a database builds the system-table indexes in that collation —
// so it cannot bootstrap an order nobody has measured.
//
// The way through is that the FORMAT allows a per-column collation even though no Microsoft tool will write
// one (page-02b §3.4). So: create the database as General v1, have ACE build an EMPTY indexed text column,
// stamp the target LCID onto that column's descriptor and the page-0 header, and only then let ACE insert.
// Index keys follow the column's collation, so ACE encodes in the target order and MSysObjects is never
// touched. Two details are load-bearing and cost a round trip each when got wrong:
//
//   * The base must already be version 1, so only the LANGID changes. ASCII primaries are identical across
//     LCIDs at a given version, so the system tables' stored keys stay valid. Stamping a v0 file to v1 would
//     invalidate every one of them — v0 primaries are a single compacted byte, v1's are 2-byte NLS pairs.
//   * Page 0 is XOR-obfuscated from 0x18 with a fixed 128-byte mask. Writing the collation in the clear
//     stores garbage that reads back as a nonsense order.
//
// This found Romanian v1, which nothing else could have. What is left is the instrument and the control:
// the surveys it drove are done, and their results are recorded in page-03-04 §10.4 and page-02b §3.4.
public class CollationV1PatchProbeTests(ITestOutputHelper output)
{
    private static readonly Collation GeneralV1 = Collation.General;
    private static readonly Collation CroatianV1 =
        new(CollatingOrder.Croatian, Collation.GeneralVersion);

    /// <summary>Words that exercise the v0-era tailorings, plus every character of Latin-1 Supplement and
    /// Latin Extended-A so a tailored letter cannot hide by simply not being sampled.</summary>
    private static readonly string[] Samples = [.. Wide()];

    private static IEnumerable<string> Wide()
    {
        foreach (string word in Words) yield return word;
        for (int c = 0x00A0; c <= 0x017F; c++)
            if (!char.IsControl((char)c)) yield return ((char)c).ToString();
    }

    /// <remarks>A property, not a field: static initialisers run in declaration order and
    /// <see cref="Samples"/> is declared first, so a field here would still be null when it builds.</remarks>
    private static string[] Words =>
    [
        "apple", "Apple", "zebra", "MSysObjects", "café", "coté", "côte", "côté", "Ångström",
        "ch", "cch", "ll", "lj", "ljubav", "nj", "njegov", "dž", "džem",
        "cs", "dz", "dzs", "gy", "ly", "ny", "sz", "ty", "zs", "aa", "Aarhus",
        "ng", "nh", "ph", "qu", "th", "tr", "gi", "kh",
    ];

    // The control. Croatian v1 is an order LibRed implements and has measured keys for, so if a stamped file
    // does not reproduce our encoder byte for byte the instrument is broken and anything else it says is
    // worthless. Run this before believing any number the screen below produces.
    [Fact]
    public void The_control_reproduces_croatian_v1_through_a_patched_file()
    {
        Assert.SkipWhen(!CroatianV1.IsIndexKeyEncodable,
            "Croatian v1 is not implemented — there is no control to measure against.");

        Dictionary<string, string> ace = AceKeysThroughPatchedFile(CroatianV1, Samples, quiet: false);
        Assert.NotEmpty(ace);

        int wrong = Matches(ace, CroatianV1) ?? -1;

        // A stamp ACE ignored would yield General v1 keys and look clean against the wrong oracle, so
        // require the target to actually DIFFER from the base the file was created in.
        int departures = Matches(ace, GeneralV1) ?? -1;
        output.WriteLine($"Croatian v1 through a patched file: {ace.Count} keys, LibRed disagrees on " +
                         $"{wrong}, differs from General v1 on {departures}");

        Assert.Equal(0, wrong);
        Assert.True(departures > 0,
            "the patched file produced General v1 keys — ACE ignored the stamp, so the route does not work");
    }

    // The re-runnable question: has any LCID gained a version-1 table? Six orders outside CJK have one —
    // General, Croatian, Bosnian, Serbian, Indic, Romanian — and every other LCID falls back to a version-0
    // table instead. Worth asking again against a new ACE, and cheap because the answer is a binary.
    //
    // Screened against General V1, never against General v0. A real v1 table is a small DELTA on General v1
    // (Croatian moves 39 of 280 samples, Romanian 10), while anything that fell back differs from it on
    // every sample, the two versions' primaries being structurally different. Screening against General v0
    // instead gives false positives AND false negatives: the neutral LCIDs fall back to their own language's
    // v0 tailoring rather than to General v0, and a small sample set cannot see a tailoring it does not
    // contain a letter for. Both mistakes were made getting here.
    [Fact]
    public void Survey_version_1_across_every_known_lcid()
    {
        string[] screen = ["a", "z", "café", "ch", "ż", "ö", "İ"];

        var report = new System.Text.StringBuilder();
        void Write(string line) { report.AppendLine(line); output.WriteLine(line); }

        var candidates = new List<string>();
        int grounded = 0, refused = 0;

        foreach (CollatingOrder order in Enum.GetValues<CollatingOrder>())
        {
            if (order == CollatingOrder.Undefined) continue;
            var target = new Collation(order, Collation.GeneralVersion);

            Dictionary<string, string> keys;
            try
            {
                keys = AceKeysThroughPatchedFile(target, screen, quiet: true);
            }
            catch (Exception)
            {
                refused++;                       // ACE will not open it — an LCID it does not recognise
                continue;
            }

            if (Matches(keys, GeneralV1) is { } delta && delta >= keys.Count) { grounded++; continue; }

            string known = Matches(keys, target) is { } n
                ? (n == 0 ? "LibRed agrees" : $"!! LibRed disagrees on {n}")
                : "NOT IMPLEMENTED";
            candidates.Add($"  {order,-34} 0x{(int)order:X4}  {known}");
            Write(candidates[^1]);
        }

        Write($"{grounded} orders fall back to a version-0 table, {refused} refused by ACE, " +
              $"{candidates.Count} carry a real version-1 table");

        string directory = Path.Combine(AppContext.BaseDirectory, "collation-survey");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "v1-all-lcids.txt"), report.ToString());

        Assert.True(grounded > 0, "nothing grounded — the screen is not measuring what it claims");
        Assert.DoesNotContain(candidates, c => c.Contains("!! LibRed disagrees", StringComparison.Ordinal));
    }

    /// <summary>How many of ACE's keys LibRed's encoder for <paramref name="collation"/> disagrees with, or
    /// null when LibRed cannot encode that collation at all.</summary>
    private static int? Matches(Dictionary<string, string> keys, Collation collation)
    {
        if (!collation.IsIndexKeyEncodable) return null;
        var column = new ColumnDef
        {
            Name = "K", Type = JetDataType.Text, Index = 0, Collation = collation,
        };
        return keys.Count(k =>
            Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [k.Key])) != k.Value);
    }

    /// <summary>
    /// Authors a General v1 database, has ACE create an empty indexed text column in it, stamps
    /// <paramref name="target"/> onto that column and the page-0 header, then has ACE fill it. Returns the
    /// index keys ACE stored, by sample.
    /// </summary>
    private Dictionary<string, string> AceKeysThroughPatchedFile(
        Collation target, string[] samples, bool quiet)
    {
        string path = TemporaryDatabase.CreatePath("v1patch-", ".accdb");
        try
        {
            DatabaseCreator.CreateEmpty(path, collation: GeneralV1);

            using (OleDbConnection connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE V1 (K TEXT(100), V LONG)");
                Exec(connection, "CREATE INDEX IX_V1 ON V1 (K)");   // empty: no key written yet
            }

            Stamp(path, "V1", "K", target);

            // Confirm the stamp landed where it was meant to, BEFORE handing the file to ACE — otherwise a
            // refusal from ACE cannot be told apart from having written the bytes into the wrong offset.
            using (var check = JetDatabase.Open(path))
            {
                Assert.Equal(target, check.Collation);
                Assert.Equal(target, check.Catalog.FindTable("V1")!.FindColumn("K")!.Collation);
            }

            using (OleDbConnection connection = AceTestDatabase.Open(path))
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    using OleDbCommand insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO V1 (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", samples[i]);
                    insert.Parameters.AddWithValue("v", i);
                    insert.ExecuteNonQuery();
                }
            }

            using var database = JetDatabase.Open(path);
            if (!quiet)
                output.WriteLine($"on disk: {database.Collation.Order} v{database.Collation.Version} " +
                                 $"sortId {database.Collation.SortId}");

            var table = database.OpenTable("V1");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_V1");
            ColumnDef key = table.Definition.FindColumn("K")!;

            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);
            var keys = new Dictionary<string, string>();
            foreach ((byte[] stored, RowId rowId) in
                     new IndexCursor(table.Channel, index.RootPage).RawEntries())
                if (rows.TryGetValue(rowId, out object?[]? values) && values[key.Index] is string text)
                    keys[text] = Convert.ToHexString(stored);
            return keys;
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Writes <paramref name="target"/> into the page-0 header and into one column's descriptor.
    /// </summary>
    private static void Stamp(string path, string tableName, string columnName, Collation target)
    {
        using var channel = PageChannel.Open(path, readOnly: false);
        JetFormatBase format = channel.Format;

        // Page 0: LANGID at 0x6E, sort id at 0x70, version at 0x71 — but everything from 0x18 is
        // XOR-obfuscated with a fixed 128-byte mask, so the value has to be masked on the way in.
        byte[] page0 = channel.ReadPage(0).Span.ToArray();
        byte[] mask = JetFormatBase.PageZeroHeaderMask.ToArray();
        int maskStart = JetFormatBase.PageZeroHeaderMaskStart;
        ushort langId = (ushort)target.Order;
        page0[0x6E] = (byte)((langId & 0xFF) ^ mask[0x6E - maskStart]);
        page0[0x6F] = (byte)((langId >> 8) ^ mask[0x6F - maskStart]);
        page0[0x70] = (byte)(target.SortId ^ mask[0x70 - maskStart]);
        page0[0x71] = (byte)(target.Version ^ mask[0x71 - maskStart]);
        channel.WritePage(0, page0);

        TableDef table = new JetCatalog(channel).FindTable(tableName)!;
        int columnIndex = table.Columns.Single(c => c.Name == columnName).Index;

        // The descriptors follow the real-index data blocks; each is ColumnDescriptorSize bytes, in column
        // order. Single-page TDEF only, which is all this probe's table needs. Not masked, unlike page 0.
        byte[] tdef = channel.ReadPage(table.DefinitionPage).Span.ToArray();
        int realIndexes = BinaryPrimitives.ReadInt32LittleEndian(
            tdef.AsSpan(format.TdefIndexCountOffset, 4));
        int descriptor = format.TdefRealIndexBlockOffset + realIndexes * format.RealIndexEntrySize
                         + columnIndex * format.ColumnDescriptorSize;

        BinaryPrimitives.WriteUInt16LittleEndian(
            tdef.AsSpan(descriptor + format.ColumnLocaleOffset, 2), (ushort)target.Order);
        tdef[descriptor + format.ColumnCollationSortIdOffset] = target.SortId;
        tdef[descriptor + format.ColumnCollationVersionOffset] = target.Version;
        channel.WritePage(table.DefinitionPage, tdef);
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
