using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// End-to-end oracle for the "General" (v1) collation. The unit tests pin the encoder against keys measured
// from ACE; this re-derives them from a live database, so a change in ACE (or a wrong assumption about which
// weight table it uses) is caught rather than absorbed.
//
// The v1 database is one LibRed creates itself (DatabaseCreator.CreateEmpty with Collation.General) — which
// is also the point: nothing else here can make one. DAO always writes v0, ignoring the application setting,
// and Access only honours "New database sort order" through its own UI. So this doubles as the test that
// LibRed's create-with-collation produces a file ACE accepts as a General database.
public class GeneralV1CollationAccessTests
{
    private static readonly string[] Samples =
    [
        "apple", "Apple", "cafe", "café", "O'Brien", "Anne-Marie", "a b", "a1",
        "Ä", "ß", "Æ", "ª", "¹", "£", "©", "«", "½", "Α", "А", "Ａ", "ﬁ", "coop", "co-op",
    ];

    /// <summary>A fresh, empty database whose default sort order is General (v1), created by LibRed.</summary>
    private static string CreateV1Database(string prefix)
    {
        string path = TemporaryDatabase.CreatePath(prefix);
        DatabaseCreator.CreateEmpty(path, collation: Collation.General);
        return path;
    }

    // ACE authors the table and index here, in a v1 database LibRed created — so the keys being compared are
    // ACE's own, written by ACE, in a file LibRed made. No external fixture involved.
    [Fact]
    public void Libred_reproduces_ace_index_keys_in_a_v1_database()
    {
        string path = CreateV1Database("v1-collation-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE V1K (K TEXT(50), V LONG)");
                Exec(connection, "CREATE INDEX IX_V1K ON V1K (K)");
                for (int i = 0; i < Samples.Length; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO V1K (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", Samples[i]);
                    insert.Parameters.AddWithValue("v", i);
                    insert.ExecuteNonQuery();
                }
            }

            using var db = JetDatabase.Open(path);
            Assert.Equal(Collation.GeneralVersion, db.DefaultCollationVersion);

            var table = db.OpenTable("V1K");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_V1K");
            ColumnDef keyColumn = table.Definition.FindColumn("K")!;
            Assert.Equal(Collation.General, keyColumn.Collation);

            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);
            int checkedKeys = 0;
            foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
            {
                if (!rows.TryGetValue(rowId, out object?[]? values)) continue;
                string value = (string?)values[keyColumn.Index] ?? "";

                var aligned = new object?[table.Definition.Columns.Count];
                aligned[keyColumn.Index] = values[keyColumn.Index];

                Assert.Equal(
                    (value, Convert.ToHexString(stored)),
                    (value, Convert.ToHexString(IndexKeyEncoder.Encode(index.Columns, aligned))));
                checkedKeys++;
            }

            Assert.Equal(Samples.Length, checkedKeys);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A LibRed-written index in a v1 database must be one ACE agrees with — the keys have to be right, and in
    // the right order, for ACE's own seeks to find the rows.
    [Fact]
    public void Ace_reads_an_index_libred_wrote_in_a_v1_database()
    {
        var column = new ColumnDef { Name = "K", Type = JetDataType.Text, Index = 0, Collation = Collation.General };
        string[] unique = Samples
            .GroupBy(v => Convert.ToHexString(IndexKeyEncoder.Encode([(column, true)], [v])))
            .Select(g => g.First())
            .ToArray();

        string path = CreateV1Database("v1-written-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("W1",
                    [new ColumnSpec("K", JetDataType.Text, 50, IsFixedLength: false),
                     new ColumnSpec("V", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["K"]);
                var table = db.OpenTable("W1");
                // Dedupe by encoded key, not by string: the collation folds case, so "apple" and "Apple"
                // are the *same* key and a unique primary key rightly rejects the second.
                for (int i = 0; i < unique.Length; i++) table.Insert([unique[i], i]);
            }

            using var connection = AceTestDatabase.Open(path);
            using (var count = connection.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM W1";
                Assert.Equal(unique.Length, Convert.ToInt32(count.ExecuteScalar()));
            }

            // Seek each value through ACE: it resolves these against the index LibRed built.
            foreach (string sample in unique)
            {
                using var seek = connection.CreateCommand();
                seek.CommandText = "SELECT COUNT(*) FROM W1 WHERE K = ?";
                seek.Parameters.AddWithValue("k", sample);
                Assert.Equal(1, Convert.ToInt32(seek.ExecuteScalar()));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The create-with-collation option itself: LibRed makes a General (v1) database, and both LibRed and
    // ACE agree that is what it is.
    [Fact]
    public void Libred_creates_a_v1_database_that_ace_opens()
    {
        string path = CreateV1Database("v1-created-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.Equal(Collation.General, db.Collation);
                Assert.Equal(Collation.GeneralVersion, db.DefaultCollationVersion);

                // A table created in it inherits v1, rather than defaulting to the engine's legacy order.
                db.CreateTable("T",
                    [new ColumnSpec("K", JetDataType.Text, 30, IsFixedLength: false)],
                    primaryKey: ["K"]);
                var table = db.OpenTable("T");
                Assert.Equal(Collation.General, table.Definition.FindColumn("K")!.Collation);
                table.Insert(["Α"]);   // a character the v0 table cannot even encode
            }

            using var connection = AceTestDatabase.Open(path);
            using var read = connection.CreateCommand();
            read.CommandText = "SELECT K FROM T";
            Assert.Equal("Α", read.ExecuteScalar());
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
