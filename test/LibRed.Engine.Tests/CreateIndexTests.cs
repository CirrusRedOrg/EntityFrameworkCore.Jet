using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class CreateIndexTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cidx-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    private static void Table(QueryEngine e) =>
        e.ExecuteNonQuery("CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY, `Name` VARCHAR(50), `Age` INTEGER)");

    [Fact]
    public void Create_index_round_trips_and_inserts_maintain_it()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                Table(e);
                e.ExecuteNonQuery("CREATE INDEX `IX_Name` ON `T` (`Name`)");
                e.ExecuteNonQuery("CREATE UNIQUE INDEX `UX_Age` ON `T` (`Age`)");
                e.ExecuteNonQuery("INSERT INTO `T` (`Id`, `Name`, `Age`) VALUES (1, 'a', 30)");
                e.ExecuteNonQuery("INSERT INTO `T` (`Id`, `Name`, `Age`) VALUES (2, 'b', 40)");
            }
            using (var db = JetDatabase.Open(path))
            {
                var t = db.Catalog.FindTable("T")!;
                Assert.Contains(t.Indexes, ix => ix.Name == "IX_Name" && !ix.IsUnique && !ix.IsPrimaryKey
                    && ix.Columns.Select(c => c.Column.Name).SequenceEqual(["Name"]));
                Assert.Contains(t.Indexes, ix => ix.Name == "UX_Age" && ix.IsUnique && !ix.IsPrimaryKey
                    && ix.Columns.Select(c => c.Column.Name).SequenceEqual(["Age"]));
                Assert.Equal(2, new QueryEngine(db).ExecuteQuery("SELECT `Id` FROM `T`").Rows.Count());
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Create_index_multi_column_and_with_primary()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE `T` (`A` INTEGER, `B` INTEGER, `C` INTEGER)");
                e.ExecuteNonQuery("CREATE INDEX `IX_AB` ON `T` (`A`, `B`)");
                e.ExecuteNonQuery("CREATE INDEX `PK_T` ON `T` (`C`) WITH PRIMARY");
            }
            using (var db = JetDatabase.Open(path))
            {
                var t = db.Catalog.FindTable("T")!;
                Assert.Contains(t.Indexes, ix => ix.Name == "IX_AB"
                    && ix.Columns.Select(c => c.Column.Name).SequenceEqual(["A", "B"]));
                Assert.Contains(t.Indexes, ix => ix.Name == "PK_T" && ix.IsPrimaryKey && ix.IsUnique);
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // WITH IGNORE NULL: the index is created (flag 0x02, reader exposes IgnoreNulls), and a row whose
    // indexed column is null is excluded from the index — the insert still succeeds.
    [Fact]
    public void With_ignore_null_creates_sparse_index_and_skips_null_rows()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                Table(e);
                e.ExecuteNonQuery("CREATE INDEX `IX_Age` ON `T` (`Age`) WITH IGNORE NULL");
                e.ExecuteNonQuery("INSERT INTO `T` (`Id`, `Name`, `Age`) VALUES (1, 'a', 30)"); // indexed
                e.ExecuteNonQuery("INSERT INTO `T` (`Id`, `Name`, `Age`) VALUES (2, 'b', NULL)"); // excluded
                e.ExecuteNonQuery("INSERT INTO `T` (`Id`, `Name`, `Age`) VALUES (3, 'c', 40)"); // indexed
            }
            using (var db = JetDatabase.Open(path))
            {
                var ix = db.Catalog.FindTable("T")!.Indexes.Single(x => x.Name == "IX_Age");
                Assert.True(ix.IgnoreNulls);
                // All rows are still in the table (the null one is just absent from the index).
                Assert.Equal(3, new QueryEngine(db).ExecuteQuery("SELECT `Id` FROM `T`").Rows.Count());
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // A descending index: the index-data block records the column as descending (Ascending = false),
    // and inserts encode reversed key bytes (IndexKeyEncoder handles the inversion).
    [Fact]
    public void Descending_index_records_direction_and_inserts()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                Table(e);
                e.ExecuteNonQuery("CREATE INDEX `IX_AgeDesc` ON `T` (`Age` DESC)");
                e.ExecuteNonQuery("INSERT INTO `T` (`Id`, `Name`, `Age`) VALUES (1, 'a', 30)");
                e.ExecuteNonQuery("INSERT INTO `T` (`Id`, `Name`, `Age`) VALUES (2, 'b', 40)");
            }
            using (var db = JetDatabase.Open(path))
            {
                var ix = db.Catalog.FindTable("T")!.Indexes.Single(x => x.Name == "IX_AgeDesc");
                var (col, ascending) = ix.Columns.Single();
                Assert.Equal("Age", col.Name);
                Assert.False(ascending); // recorded as descending
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // CREATE INDEX on a populated table back-fills the new index over the existing rows.
    [Fact]
    public void Create_index_on_non_empty_table_backfills()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                Table(e);
                e.ExecuteNonQuery("INSERT INTO `T` (`Id`, `Name`, `Age`) VALUES (1, 'a', 30)");
                e.ExecuteNonQuery("INSERT INTO `T` (`Id`, `Name`, `Age`) VALUES (2, 'b', 40)");
                e.ExecuteNonQuery("CREATE INDEX `IX_Name` ON `T` (`Name`)"); // no throw
            }

            using (var db = JetDatabase.Open(path))
            {
                var e = new QueryEngine(db);
                Assert.Equal(2, e.ExecuteQuery("SELECT * FROM `T`").Rows.Count()); // rows intact
                Assert.Equal(1, e.ExecuteQuery("SELECT `Id` FROM `T` WHERE `Name` = 'b'").Rows.Count());
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
