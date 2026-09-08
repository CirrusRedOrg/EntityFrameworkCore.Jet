using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: do the General-Legacy (v0) and General (v1) sort orders actually produce different index keys?
//
// The version byte at column 0x0E / page-0 0x71 is known to exist and to read 0/1. What has never been shown
// from bytes in this repo is that it selects a *weight table* rather than being metadata. This builds the same
// indexed text column, with the same values, in a v0 and a v1 database (via ACE so the engine does the
// encoding) and diffs the stored keys.
//
// The v1 database comes from LIBRED_V1_PROBE — Access itself must create it (Access.Application
// .NewCurrentDatabase honours the "New Database Sort Order" option; DAO ignores it and always writes v0).
public class CollationVersionDiffProbeTest(ITestOutputHelper output)
{
    private static readonly string[] Samples =
    [
        "apple", "Apple", "cafe", "café", "O'Brien", "Anne-Marie", "a b", "a1",
        "Ä", "ß", "Æ", "Ø", "ª", "¹", "£", "©", "«", "½",
        "Α", "α",                 // Greek capital/small alpha
        "А", "а",                 // Cyrillic capital/small A
        "Ａ", "　", "ﬁ",       // fullwidth A, ideographic space, ligature fi
        "­", "coop", "co-op",
    ];

    [Fact]
    public void Probe_v0_versus_v1_index_keys()
    {
        string? v1Source = Environment.GetEnvironmentVariable("LIBRED_V1_PROBE");
        if (v1Source is null || !File.Exists(v1Source))
        {
            output.WriteLine("LIBRED_V1_PROBE not set to an existing v1 database — skipping.");
            return;
        }

        Dictionary<string, string> v0 = KeysFor(TestDatabases.NorthwindAccdb, "collation-v0-", out byte v0Version);
        Dictionary<string, string> v1 = KeysFor(v1Source, "collation-v1-", out byte v1Version);
        output.WriteLine($"v0 database reports version {v0Version}; v1 database reports version {v1Version}");
        output.WriteLine("");

        int differing = 0;
        foreach (string sample in Samples)
        {
            v0.TryGetValue(sample, out string? a);
            v1.TryGetValue(sample, out string? b);
            bool same = a is not null && a == b;
            if (!same) differing++;
            output.WriteLine($"{Describe(sample),-26} v0 {a ?? "(none)",-30} v1 {b ?? "(none)",-30} {(same ? "" : "  <-- DIFFERS")}");
        }

        output.WriteLine("");
        output.WriteLine(differing == 0
            ? "=> identical keys for every sample: the version byte did not change the encoding for these values."
            : $"=> {differing} of {Samples.Length} samples encode differently: the version byte selects a weight table.");
    }

    /// <summary>Builds an indexed text column in a copy of <paramref name="source"/> through ACE, then reads the
    /// stored index keys back with LibRed, mapped by the value that produced them.</summary>
    private static Dictionary<string, string> KeysFor(string source, string prefix, out byte version)
    {
        string path = TemporaryDatabase.CopyPath(source, prefix);
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE CollProbe (K TEXT(50), V LONG)");
                Exec(connection, "CREATE INDEX IX_CollProbe ON CollProbe (K)");
                for (int i = 0; i < Samples.Length; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO CollProbe (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", Samples[i]);
                    insert.Parameters.AddWithValue("v", i);
                    try { insert.ExecuteNonQuery(); } catch (Exception) { /* ACE refused this value */ }
                }
            }

            using var db = JetDatabase.Open(path);
            version = db.DefaultCollationVersion;
            var table = db.OpenTable("CollProbe");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_CollProbe");
            ColumnDef keyColumn = table.Definition.FindColumn("K")!;
            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);

            var keys = new Dictionary<string, string>();
            foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
                if (rows.TryGetValue(rowId, out object?[]? values))
                    keys[(string?)values[keyColumn.Index] ?? ""] = Convert.ToHexString(stored);
            return keys;
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string Describe(string s) =>
        s.All(c => c is >= ' ' and <= '~') ? $"\"{s}\"" : string.Concat(s.Select(c => $"U+{(int)c:X4}"));

    private static void Exec(System.Data.OleDb.OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
