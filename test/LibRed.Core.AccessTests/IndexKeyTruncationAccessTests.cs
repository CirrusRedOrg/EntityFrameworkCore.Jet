using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using LibRed.Tests.Shared;
using Xunit;

namespace LibRed.Core.Tests;

// Past 510 bytes ACE keeps the first 508 of an index key and replaces the rest with a 2-byte checksum over
// what it dropped. The checksum skips the final discarded byte — the spec's "for each dropped byte b, except
// the terminator", justified with "it is 0x00 anyway".
//
// That justification holds only when the key's LAST column is text. With a numeric tail the final byte is a
// data byte, and every measurement behind the rule happened to use an all-text key. These tests pin down both
// halves against ACE, because a wrong key here is the silent kind of wrong: neither engine errors, ACE writes
// its own key into the same index, and seeks quietly miss rows.
public class IndexKeyTruncationAccessTests(ITestOutputHelper output)
{
    [Fact]
    public void An_all_text_key_past_the_cap_matches_ACE()
    {
        string path = Build("trunc-text-", "A TEXT(255), B TEXT(255), K LONG", "(A, B)",
            i => [new string('a', 255), new string('b', 250) + i, i]);
        try { Assert.Equal(0, CompareKeys(path, ["A", "B"])); }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Below the cap the very same column shape agrees, which is what establishes that the encoding is right
    // and that the disagreement above is about truncation alone.
    [Fact]
    public void A_numeric_tailed_key_below_the_cap_matches_ACE()
    {
        string path = Build("trunc-short-", "A TEXT(50), B TEXT(50), N LONG, K LONG", "(A, B, N)",
            i => ["alpha", "beta" + i, 1000 + i, i]);
        try { Assert.Equal(0, CompareKeys(path, ["A", "B", "N"])); }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The case that exposed the checksum's real rule. The final discarded byte is XORed into the high half
    // rather than skipped; skipping it is only equivalent when that byte is the 0x00 text terminator, which
    // is why every all-text measurement agreed and this one did not. Three numeric types, so the fix is not
    // pinned to Int32's byte pattern.
    [Theory]
    [InlineData("N LONG")]
    [InlineData("N CURRENCY")]
    [InlineData("N DOUBLE")]
    public void A_numeric_tailed_key_past_the_cap_matches_ACE(string tail)
    {
        object[] values = [1, 255, 65535, 16777215];
        string path = Build("trunc-numeric-", $"A TEXT(255), B TEXT(255), {tail}, K LONG", "(A, B, N)",
            i => [new string('a', 255), new string('b', 249) + (char)('a' + i), values[i], i]);
        try { Assert.Equal(0, CompareKeys(path, ["A", "B", "N"])); }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A hyphen or apostrophe emits an inline word-sort record into the key's trailing section, which for a
    // long value always lands in the part ACE drops. That case was refused for years on the reasoning that
    // the record is unobservable and that ACE might recompute its position when truncating. Neither holds:
    // the position byte tracks where the mark actually sat, and the checksum over LibRed's reconstruction
    // matches ACE's. Both mark characters, and positions spread across the value, because one position could
    // not tell a correct reconstruction from a coincidentally harmless one.
    [Theory]
    [InlineData('-', 2)]
    [InlineData('-', 60)]
    [InlineData('-', 200)]
    [InlineData('-', 248)]
    [InlineData('\'', 6)]
    [InlineData('\'', 120)]
    [InlineData('\'', 240)]
    public void A_key_whose_dropped_bytes_hold_a_word_sort_record_matches_ACE(char mark, int offset)
    {
        string b = new string('b', 250);
        b = b[..offset] + mark + b[(offset + 1)..] + "z";
        string path = Build($"trunc-wordsort-{offset}-", "A TEXT(255), B TEXT(255), K LONG", "(A, B)",
            i => [new string('a', 255), b + (char)('a' + i), i]);
        try { Assert.Equal(0, CompareKeys(path, ["A", "B"])); }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Creates the table, indexes it, and lets ACE write the keys.</summary>
    private static string Build(string prefix, string columns, string indexColumns, Func<int, object[]> row)
    {
        string path = TemporaryDatabase.CreatePath(prefix);
        DatabaseCreator.CreateEmpty(path);
        using var connection = AceTestDatabase.Open(path);
        Exec(connection, $"CREATE TABLE Probe ({columns})");
        Exec(connection, $"CREATE INDEX IX ON Probe {indexColumns}");

        string names = string.Join(", ", columns.Split(',').Select(c => c.Trim().Split(' ')[0]));
        string placeholders = string.Join(", ", names.Split(',').Select(_ => "?"));
        for (int i = 0; i < 4; i++)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = $"INSERT INTO Probe ({names}) VALUES ({placeholders})";
            foreach (object value in row(i)) insert.Parameters.AddWithValue("p", value);
            insert.ExecuteNonQuery();
        }
        return path;
    }

    /// <summary>How many of ACE's index entries LibRed re-encodes differently.</summary>
    private int CompareKeys(string path, string[] keyColumns)
    {
        using var db = JetDatabase.Open(path);
        Table table = db.OpenTable("Probe");
        IndexDef index = table.Definition.Indexes.Single(i =>
            string.Equals(i.Name, "IX", StringComparison.OrdinalIgnoreCase));
        var columns = keyColumns.Select(c => (table.Definition.FindColumn(c)!, true)).ToList();
        var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);

        int compared = 0, mismatched = 0;
        foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
        {
            if (!rows.TryGetValue(rowId, out object?[]? values)) continue;
            compared++;
            string ace = Convert.ToHexString(stored);
            string ours = Convert.ToHexString(IndexKeyEncoder.Encode(columns, values));
            if (ace == ours) continue;
            mismatched++;
            output.WriteLine($"ACE {ace}");
            output.WriteLine($"LR  {ours}");
        }
        Assert.True(compared > 0, "no index entries were compared");
        return mismatched;
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
