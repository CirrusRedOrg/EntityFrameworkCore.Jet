using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: what does a non-General sort order look like on disk, and how is a contraction encoded?
//
// Spanish Traditional treats "ch" and "ll" as single letters sorting after "c" and "l"; Spanish Modern (the
// 1994 reform) sorts them as the plain letter pairs. Access offers both, and they are otherwise the same
// locale — so diffing their index keys isolates *contraction* (several characters collapsing to one primary
// weight), the one primitive neither JetTextCollation nor JetTextCollationV1 implements.
//
// Two things are open before any encoder work:
//   1. Which LCID each is recorded as. DAO's enum has a single dbSortSpanish = 1034 (0x040A = Spanish
//      Traditional). Modern is 0x0C0A = 3082 in Windows, which is not in the enum at all — so either ACE
//      records 3082, or it records 1034 for both and distinguishes them with the sort-order *version* byte,
//      the way it already does for General v0/v1.
//   2. Whether the digraphs and "n" take the primary weights left free by the v0 letter table. It steps by
//      +2 almost everywhere, leaving 0x4E between C and D, 0x5F between L and M, and 0x63 between N and O.
//      Gaps are the norm rather than a Spanish reservation, so landing on exactly those three would be a
//      real result: it would make the compacted v0 table's gaps insertion slots for language letters.
public class SpanishCollationProbeTest(ITestOutputHelper output)
{
    // Single letters and digraphs read the weights directly; the words show the ordering they produce.
    private static readonly string[] Samples =
    [
        "c", "ch", "d", "l", "ll", "m", "n", "ñ", "o",
        "C", "CH", "Ch", "L", "LL", "Ll", "N", "Ñ",
        "cielo", "cuna", "chico", "danza",
        "luna", "lupa", "llama", "mano",
        "nube", "nuez", "ñu", "orilla",
    ];

    [Fact]
    public void Probe_spanish_traditional_versus_modern()
    {
        foreach ((string label, string path) in Fixtures())
            ReportHeader(label, path);

        var keys = new Dictionary<string, Dictionary<string, string>>();
        foreach ((string label, string path) in Fixtures())
            keys[label] = KeysFor(label, path);

        string[] labels = Fixtures().Select(f => f.Label).ToArray();
        output.WriteLine("");
        output.WriteLine("index keys (ACE-encoded, read back by LibRed):");
        output.WriteLine($"  {"value",-10} {string.Join(" ", labels.Select(l => $"{l,-28}"))}");
        int differing = 0;
        foreach (string sample in Samples)
        {
            string?[] row = labels.Select(l => keys[l].GetValueOrDefault(sample)).ToArray();
            bool same = row.All(k => k is not null && k == row[0]);
            if (!same) differing++;
            output.WriteLine($"  {sample,-10} {string.Join(" ", row.Select(k => $"{k ?? "(none)",-28}"))}" +
                             (same ? "" : "  <-- DIFFERS"));
        }

        output.WriteLine("");
        output.WriteLine(differing == 0
            ? "=> identical keys throughout: the orders encode the same, so the difference is not in the keys."
            : $"=> {differing} of {Samples.Length} samples encode differently across the {labels.Length} orders.");
    }

    private static IEnumerable<(string Label, string Path)> Fixtures()
    {
        yield return ("Traditional", TestDatabases.SpanishTraditionalAccdb);
        yield return ("Modern", TestDatabases.SpanishModernAccdb);
        // The General (v0) baseline, so "n-with-tilde is a letter in Spanish" is read off bytes rather than
        // inferred from JetTextCollation taking the decomposition path for it.
        yield return ("General v0", TestDatabases.NorthwindAccdb);
    }

    /// <summary>Reports the database-wide sort order and every text column's own collation, so a per-column
    /// override would be visible rather than assumed away.</summary>
    private void ReportHeader(string label, string path)
    {
        if (!File.Exists(path)) { output.WriteLine($"{label}: missing ({path})"); return; }

        using var db = JetDatabase.Open(path);
        output.WriteLine($"{label}: LCID {db.DefaultCollationLcid} (0x{db.DefaultCollationLcid:X4}) " +
                         $"version {db.DefaultCollationVersion}  [{db.Collation}]");

        foreach (TableDef definition in db.Catalog.UserTables)
        {
            var text = definition.Columns
                .Where(c => c.Type is JetDataType.Text or JetDataType.Memo)
                .ToList();
            if (text.Count == 0) continue;
            output.WriteLine($"   table {definition.Name}: " +
                             string.Join(", ", text.Select(c => $"{c.Name} {c.Collation}")));
        }
    }

    /// <summary>Builds an indexed text column through ACE — so ACE's own engine does the encoding — then reads
    /// the stored keys back with LibRed, mapped by the value that produced them.</summary>
    private Dictionary<string, string> KeysFor(string label, string source)
    {
        var keys = new Dictionary<string, string>();
        if (!File.Exists(source)) return keys;

        string path = TemporaryDatabase.CopyPath(source, $"spanish-{label.ToLowerInvariant()}-");
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

                using var select = connection.CreateCommand();
                select.CommandText = "SELECT K FROM CollProbe ORDER BY K";
                using var reader = select.ExecuteReader();
                var ordered = new List<string>();
                while (reader.Read()) ordered.Add(reader.GetString(0));
                output.WriteLine($"{label} ORDER BY: {string.Join(" ", ordered)}");
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("CollProbe");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_CollProbe");
            ColumnDef keyColumn = table.Definition.FindColumn("K")!;
            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);

            foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
                if (rows.TryGetValue(rowId, out object?[]? values))
                    keys[(string?)values[keyColumn.Index] ?? ""] = Convert.ToHexString(stored);
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
