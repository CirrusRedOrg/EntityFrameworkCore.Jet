using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: exactly how a contraction is encoded, before implementing one.
//
// A contraction is several characters weighing as one letter. Ten sort orders need it and nothing else, so
// it is the single primitive that unlocks the most. But the summary diff left one thing unreconciled:
// Hungarian "ny" is a clean single primary (63 06, no trailing y) while "gy" looked like 56 03 76 - a
// two-byte primary AND a trailing y. Either the digraph set is not uniform, or that reading was wrong.
//
// So: for each order, the component letters on their own, every digraph, the doubled forms (Hungarian writes
// a doubled digraph by doubling only its first letter - "ggy" is "gy"+"gy", not "g"+"gy"), and real words.
// Printed in full, no capping, so the structure is visible rather than inferred.
public class ContractionProbeTest(ITestOutputHelper output)
{
    private static readonly (string Fixture, string[] Samples)[] Cases =
    [
        ("Hungarian", [
            "c", "s", "z", "d", "g", "y", "n", "t", "l",
            "cs", "dz", "dzs", "gy", "ly", "ny", "sz", "ty", "zs",
            "ccs", "ddz", "ggy", "lly", "nny", "ssz", "tty", "zzs",
            "cukor", "csak", "gyar", "nagy", "meggy", "asszony", "gy", "gz", "gyy",
        ]),
        ("Czech", [
            "c", "h", "s", "z", "r", "ch", "cch", "chch", "hc",
            "cukr", "chata", "hodina", "chch",
        ]),
        ("CroatianLegacy", [
            "d", "z", "l", "n", "j", "dz", "dž", "lj", "nj", "ddž", "llj", "nnj", "dzz",
            "ljubav", "njegov", "džem",
        ]),
        ("SpanishTraditional", [
            "c", "h", "l", "ch", "ll", "cch", "lll", "chh", "llll",
            "chico", "llama", "coche", "calle",
        ]),
        // Vietnamese turned out to be a digraph order too — "Ångström" showed "ng" and "tr" each weighing as
        // one letter, which a single-character sweep could never have revealed.
        ("Vietnamese", [
            "c", "g", "h", "i", "k", "n", "p", "q", "t", "u", "r",
            "ch", "gh", "gi", "kh", "ng", "ngh", "nh", "ph", "qu", "th", "tr",
            "nng", "ngg", "ngstr", "nghi", "nghe", "nga", "nhe", "quy", "tre", "thu",
        ]),
        ("NorwegianDanish", [
            "a", "aa", "aaa", "å", "aab", "ab", "baa", "Aa", "AA",
            // Where does the secondary land relative to a TWO-BYTE primary? "å" and "æ" are two-byte
            // primaries with a default secondary; "ö" is a two-byte primary carrying 0x13. Vary how many of
            // each precede the accented one, and the index the accent lands on tells us what the section
            // counts: characters, primary weights, or primary bytes.
            "ö", "bö", "öb", "bbö", "aö", "åö", "ååö", "æö", "aaö", "bäb", "Ångström", "ånö", "nåö",
        ]),
    ];

    [Fact]
    public void Probe_how_contractions_encode()
    {
        foreach ((string fixture, string[] samples) in Cases)
        {
            string source = TestDatabases.Data($"{fixture}.accdb");
            if (!File.Exists(source)) { output.WriteLine($"{fixture}: missing"); continue; }

            string path = TemporaryDatabase.CopyPath(source, $"contraction-{fixture.ToLowerInvariant()}-");
            try
            {
                Dictionary<string, string> keys = AceKeys(path, samples);
                using var db = JetDatabase.Open(path);
                output.WriteLine("");
                output.WriteLine($"{fixture} — {db.Collation.Order} v{db.Collation.Version}:");
                foreach (string sample in samples.Distinct())
                    output.WriteLine($"   {sample,-10} {keys.GetValueOrDefault(sample) ?? "(refused)"}");
            }
            finally { TemporaryDatabase.Delete(path); }
        }
    }

    private static Dictionary<string, string> AceKeys(string path, string[] samples)
    {
        using (var connection = AceTestDatabase.Open(path))
        {
            Exec(connection, "CREATE TABLE Contr (K TEXT(60), V LONG)");
            Exec(connection, "CREATE INDEX IX_Contr ON Contr (K)");
            int i = 0;
            foreach (string sample in samples.Distinct())
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO Contr (K, V) VALUES (?, ?)";
                insert.Parameters.AddWithValue("k", sample);
                insert.Parameters.AddWithValue("v", i++);
                try { insert.ExecuteNonQuery(); } catch (Exception) { /* ACE refused this value */ }
            }
        }

        using var db = JetDatabase.Open(path);
        var table = db.OpenTable("Contr");
        IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_Contr");
        ColumnDef keyColumn = table.Definition.FindColumn("K")!;
        var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);

        var keys = new Dictionary<string, string>();
        foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
            if (rows.TryGetValue(rowId, out object?[]? values) && values[keyColumn.Index] is string text)
                keys[text] = Convert.ToHexString(stored);
        return keys;
    }

    private static void Exec(System.Data.OleDb.OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
