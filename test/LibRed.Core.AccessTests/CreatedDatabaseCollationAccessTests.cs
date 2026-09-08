using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// LibRed synthesises a new .accdb page by page rather than copying a packaged empty file, so the collating
// order is a parameter of creation rather than a property of a template. This asserts that for EVERY order
// LibRed claims to encode — not just the two General ones it was demonstrated with.
//
// The claim is worth a test rather than an inference because creation and collation are entangled: the
// system-table indexes are built on the way, with that order's keys, so a database cannot be created in an
// order whose keys cannot be encoded. That circularity is why measuring French needed DAO to author the file
// first, and it means "creates the file" and "gets the order right" are not separable properties.
//
// The bar is not that the file opens. It is that ACE will CREATE AN INDEX in it and write keys that match
// LibRed's own — two engines agreeing on a shared index, which is the only check that catches a wrong key,
// since a disagreement does not error, it just makes seeks miss rows.
public class CreatedDatabaseCollationAccessTests(ITestOutputHelper output)
{
    /// <summary>Every collation LibRed claims to encode, found by asking rather than by keeping a list that
    /// could fall out of step with <c>JetLocaleTailoring</c>.</summary>
    public static TheoryData<int, byte, byte> EncodableCollations()
    {
        var data = new TheoryData<int, byte, byte>();
        foreach (CollatingOrder order in Enum.GetValues<CollatingOrder>())
            foreach (byte version in (byte[])[0, 1])
                foreach (byte sortId in (byte[])[0, 1])
                    if (new Collation(order, version, sortId).IsIndexKeyEncodable)
                        data.Add((int)order, version, sortId);
        return data;
    }

    [Theory]
    [MemberData(nameof(EncodableCollations))]
    public void LibRed_creates_a_database_that_ACE_indexes(int order, byte version, byte sortId)
    {
        var collation = new Collation((CollatingOrder)order, version, sortId);
        string path = TemporaryDatabase.CreatePath($"created-{order}-{version}-{sortId}-");
        try
        {
            DatabaseCreator.CreateEmpty(path, collation: collation);

            // The order has to survive the round trip, or everything below is measuring the wrong thing.
            using (var db = JetDatabase.Open(path))
                Assert.Equal(collation, db.Collation);

            // Words that exercise the tailorings across the set: accented Latin, the digraphs, a word-sort
            // ignorable, and Thai's leading vowel. Any order encodes all of them; what differs is the keys.
            string[] samples =
            [
                "apple", "café", "coté", "côte", "Ångström", "co-op", "O'Brien",
                "ñ", "č", "ž", "lj", "dž", "ch", "ll", "ı", "İ", "å", "ø", "ß",
                "เก", "ไทย", "Ω", "б", "א",
            ];

            Dictionary<string, string> ace = AceKeys(path, samples);
            Assert.NotEmpty(ace);

            var column = new ColumnDef
            {
                Name = "K", Type = JetDataType.Text, Index = 0, Collation = collation,
            };

            var mismatches = new List<string>();
            foreach (string text in samples)
            {
                if (!ace.TryGetValue(text, out string? stored)) continue;   // ACE refused the value
                string ours = Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [text]));
                if (ours != stored) mismatches.Add($"  {text,-12} ACE {stored,-30} LibRed {ours}");
            }

            output.WriteLine($"{collation.Order} v{version}" + (sortId == 0 ? "" : $" sort id {sortId}") +
                             $": ACE indexed {ace.Count} of {samples.Length} values into a LibRed-created " +
                             $"database, {mismatches.Count} disagreeing");
            foreach (string line in mismatches) output.WriteLine(line);
            Assert.Empty(mismatches);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static Dictionary<string, string> AceKeys(string path, string[] samples)
    {
        using (var connection = AceTestDatabase.Open(path))
        {
            Exec(connection, "CREATE TABLE Probe (K TEXT(50), V LONG)");
            Exec(connection, "CREATE INDEX IX_Probe ON Probe (K)");
            for (int i = 0; i < samples.Length; i++)
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO Probe (K, V) VALUES (?, ?)";
                insert.Parameters.AddWithValue("k", samples[i]);
                insert.Parameters.AddWithValue("v", i);
                try { insert.ExecuteNonQuery(); } catch (Exception) { /* ACE refused this value */ }
            }
        }

        using var db = JetDatabase.Open(path);
        var table = db.OpenTable("Probe");
        IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_Probe");
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
