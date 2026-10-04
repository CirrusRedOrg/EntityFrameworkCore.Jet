using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Each index carries its own usage map (index-data block <c>+0x22</c>) recording the pages of its B-tree.
/// Access marks the root at CREATE and adds every page a split allocates, so the map covers the whole tree.
/// Verified against ACE: the union of a table's index maps equals exactly the set of index pages ACE itself
/// marks, and LibRed reproduces that.
/// </summary>
[Collection(AceCollection.Name)]
public class IndexUsageMapTests(ITestOutputHelper output) : TempDatabaseTest
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    /// <summary>Every index page (types 0x03/0x04) in the file owned by the table's TDEF, by owner stamp.</summary>
    private static SortedSet<int> IndexPagesOwnedByTable(string path, Table table, JetFormatBase format)
    {
        long pageCount = new FileInfo(path).Length / format.PageSize;
        var pages = new SortedSet<int>();
        for (int p = 1; p < pageCount; p++)
        {
            ReadOnlySpan<byte> span = table.Channel.ReadPage(p).Span;
            if (PageHeader.ReadType(span) is not (PageType.IntermediateIndexPage or PageType.LeafIndexPage)) continue;
            if (IndexTree.ReadOwner(span, format) == table.Definition.DefinitionPage)
                pages.Add(p);
        }
        return pages;
    }

    /// <summary>The union of every index's own usage map.</summary>
    private static SortedSet<int> UnionOfIndexMaps(Table table)
    {
        var maps = new UsageMap(table.Channel, table.Definition);
        return [.. table.Definition.Indexes.SelectMany(i => maps.PagesInMap(i.UsageMap.Row, i.UsageMap.Page))];
    }

    [Fact]
    public void An_index_usage_map_covers_every_btree_page_after_splits()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "idxmap-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("T",
                [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new("V", JetDataType.Int32, 4, IsFixedLength: true)],
                primaryKey: ["Id"]);
            db.CreateIndex("T", "IX_V", [("V", false)]);
            var table = db.OpenTable("T");

            var row = new object?[2];
            for (int i = 0; i < 4000; i++) // enough to split both B-trees several levels
            {
                row[0] = i;
                row[1] = i * 7 % 4000; // scatter V so its index isn't a degenerate append
                table.Insert(row);
            }

            SortedSet<int> actual = IndexPagesOwnedByTable(path, table, db.Format);
            SortedSet<int> mapped = UnionOfIndexMaps(table);

            Assert.True(actual.Count > 2, "expected the B-trees to have split beyond their creation roots");
            Assert.Equal(actual, mapped); // no page missing, none spurious
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Index_map_coverage_matches_what_access_itself_marks()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "idxmap-ace-");
        try
        {
            using (var connection = OpenOleDb(path))
            {
                using (var c = connection.CreateCommand()) { c.CommandText = "CREATE TABLE T (Id LONG CONSTRAINT PK PRIMARY KEY, V LONG)"; c.ExecuteNonQuery(); }
                using (var c = connection.CreateCommand()) { c.CommandText = "CREATE INDEX IX_V ON T (V)"; c.ExecuteNonQuery(); }
                using var ins = connection.CreateCommand();
                ins.CommandText = "INSERT INTO T (Id, V) VALUES (?, ?)";
                var id = ins.CreateParameter(); ins.Parameters.Add(id);
                var v = ins.CreateParameter(); ins.Parameters.Add(v);
                for (int i = 0; i < 4000; i++) { id.Value = i; v.Value = i * 7 % 4000; ins.ExecuteNonQuery(); }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("T");

            // Access's own map must cover exactly the index pages it wrote — the invariant LibRed reproduces.
            Assert.Equal(IndexPagesOwnedByTable(path, table, db.Format), UnionOfIndexMaps(table));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // What DROP INDEX does with the pages the map covers. A multi-page B-tree is recorded nowhere else, so an
    // engine that frees only the root strands every other page: nothing owns them and nothing can hand them out
    // again. Whether ACE clears the map's bits, and what it leaves in the row, is measured by the same
    // whole-file diff the drops use.
    [Fact]
    public void Dropping_a_multi_page_index_releases_what_ace_releases()
    {
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "idxmap-dropix-start-");
        using (var connection = OpenOleDb(start))
        {
            using (var c = connection.CreateCommand()) { c.CommandText = "CREATE TABLE T (Id LONG CONSTRAINT PK PRIMARY KEY, V TEXT(200))"; c.ExecuteNonQuery(); }
            using (var c = connection.CreateCommand()) { c.CommandText = "CREATE INDEX IX_V ON T (V)"; c.ExecuteNonQuery(); }
            using var ins = connection.CreateCommand();
            ins.CommandText = "INSERT INTO T (Id, V) VALUES (?, ?)";
            var id = ins.CreateParameter(); ins.Parameters.Add(id);
            var v = ins.CreateParameter(); ins.Parameters.Add(v);
            for (int i = 0; i < 600; i++)
            {
                id.Value = i;
                v.Value = (i * 7 % 600).ToString("D4") + new string((char)('a' + i % 26), 196);
                ins.ExecuteNonQuery();
            }
        }

        // The index has to span more than its root, or the test measures nothing.
        using (var built = JetDatabase.Open(start))
            Assert.True(UnionOfIndexMaps(built.OpenTable("T")).Count > 3,
                "expected the indexes to have split beyond their creation roots");

        string ace = TemporaryDatabase.CopyPath(start, "idxmap-dropix-ace-");
        using (var connection = OpenOleDb(ace))
        using (var c = connection.CreateCommand())
        {
            c.CommandText = "DROP INDEX IX_V ON T";
            c.ExecuteNonQuery();
        }

        string libred = TemporaryDatabase.CopyPath(start, "idxmap-dropix-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
            Assert.True(db.DropIndex("T", "IX_V"));

        string difference = DropTableParityAccessTests.Difference(ace, libred);
        output.WriteLine(difference);
        Assert.Equal("", difference);
    }

    // The index a relationship owns is an index like any other, and DROP CONSTRAINT takes the same route: the
    // whole B-tree freed, the map record retired. Its keys are 4,000 rows of one parent id, so the tree spans
    // several pages while the parent holds a single row.
    [Fact]
    public void Dropping_a_relationship_releases_its_indexs_tree_as_ace_does()
    {
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "idxmap-dropfk-start-");
        using (var connection = OpenOleDb(start))
        {
            foreach (string sql in new[]
            {
                "CREATE TABLE P (Id LONG CONSTRAINT ppk PRIMARY KEY)",
                "CREATE TABLE C (Id LONG CONSTRAINT cpk PRIMARY KEY, PId LONG, "
                    + "CONSTRAINT fk FOREIGN KEY (PId) REFERENCES P (Id))",
                "INSERT INTO P (Id) VALUES (1)",
            })
                using (var c = connection.CreateCommand()) { c.CommandText = sql; c.ExecuteNonQuery(); }

            using var ins = connection.CreateCommand();
            ins.CommandText = "INSERT INTO C (Id, PId) VALUES (?, 1)";
            var id = ins.CreateParameter(); ins.Parameters.Add(id);
            for (int i = 0; i < 4000; i++) { id.Value = i; ins.ExecuteNonQuery(); }
        }

        using (var built = JetDatabase.Open(start))
            Assert.True(UnionOfIndexMaps(built.OpenTable("C")).Count > 3,
                "expected the indexes to have split beyond their creation roots");

        string ace = TemporaryDatabase.CopyPath(start, "idxmap-dropfk-ace-");
        using (var connection = OpenOleDb(ace))
        using (var c = connection.CreateCommand())
        {
            c.CommandText = "ALTER TABLE C DROP CONSTRAINT fk";
            c.ExecuteNonQuery();
        }

        string libred = TemporaryDatabase.CopyPath(start, "idxmap-dropfk-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
            Assert.True(db.DropConstraint("C", "fk"));

        string difference = DropTableParityAccessTests.Difference(ace, libred);
        output.WriteLine(difference);
        Assert.Equal("", difference);
    }

    // A dropped index leaves its usage-map row behind, and ACE never hands it out again: the next CREATE INDEX
    // appends a new row after the last one. Deriving the row from the table's shape instead picks a row that
    // is already taken once an index has been dropped — with no long-value column it is another live index's,
    // so two indexes share one map.
    //
    // The dropped index's logical index_num is left free too, and ACE hands out the lowest free number, so the
    // same drops decide the number a foreign key takes, on the child and on the parent — and, through it, the
    // name of an incoming block. The blocks are kept in name order ignoring case, which is where the new one
    // lands among them.
    [Theory]
    [InlineData("no long-value column",
        "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, A LONG, B LONG);CREATE INDEX IX1 ON T (A);CREATE INDEX IX2 ON T (B);DROP INDEX IX1 ON T",
        "index", "IX3", "A")]
    [InlineData("memo declared with the table",
        "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, A LONG, M MEMO);CREATE INDEX IX1 ON T (A);DROP INDEX IX1 ON T",
        "index", "IX3", "A")]
    [InlineData("the primary key recreated",
        "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, A LONG);CREATE INDEX IX1 ON T (A);DROP INDEX pk ON T",
        "primary key", "pk", "Id")]
    // Numbers 1 and 2 free below a live 3: the lowest free number and the last one dropped part company.
    [InlineData("two gaps",
        "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, A LONG, B LONG, C LONG);CREATE INDEX IX1 ON T (A);CREATE INDEX IX2 ON T (B);CREATE INDEX IX3 ON T (C);DROP INDEX IX1 ON T;DROP INDEX IX2 ON T",
        "index", "IX4", "A")]
    // Ordinal order puts IX2 before a3, case-blind order after it.
    [InlineData("a lowercase name among uppercase ones",
        "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, A LONG, B LONG);CREATE INDEX IX1 ON T (A);CREATE INDEX IX2 ON T (B);DROP INDEX IX1 ON T",
        "index", "a3", "A")]
    [InlineData("a foreign key after a drop on the child",
        "CREATE TABLE P (Id LONG CONSTRAINT pkP PRIMARY KEY);CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, A LONG, B LONG, PId LONG);CREATE INDEX IX1 ON T (A);CREATE INDEX IX2 ON T (B);DROP INDEX IX1 ON T",
        "foreign key", "fk", "PId")]
    [InlineData("a foreign key after a drop on the parent",
        "CREATE TABLE P (Id LONG CONSTRAINT pkP PRIMARY KEY, A LONG, B LONG);CREATE INDEX IXA ON P (A);CREATE INDEX IXB ON P (B);DROP INDEX IXA ON P;CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, PId LONG)",
        "foreign key", "fk", "PId")]
    [InlineData("a self-referencing foreign key after a drop",
        "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, A LONG, B LONG, PId LONG);CREATE INDEX IX1 ON T (A);CREATE INDEX IX2 ON T (B);DROP INDEX IX1 ON T",
        "self-referencing foreign key", "fk", "PId")]
    public void A_new_index_after_a_drop_is_written_as_ace_writes_it(
        string label, string create, string kind, string index, string column)
    {
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "idxmap-drop-start-");
        using (OleDbConnection connection = AceTestDatabase.Open(start))
            foreach (string statement in create.Split(';'))
                using (var c = connection.CreateCommand()) { c.CommandText = statement; c.ExecuteNonQuery(); }

        string ace = TemporaryDatabase.CopyPath(start, "idxmap-drop-ace-");
        using (OleDbConnection connection = AceTestDatabase.Open(ace))
        using (var c = connection.CreateCommand())
        {
            c.CommandText = kind switch
            {
                "primary key" => $"ALTER TABLE T ADD CONSTRAINT {index} PRIMARY KEY ({column})",
                "foreign key" => $"ALTER TABLE T ADD CONSTRAINT {index} FOREIGN KEY ({column}) REFERENCES P (Id)",
                "self-referencing foreign key" => $"ALTER TABLE T ADD CONSTRAINT {index} FOREIGN KEY ({column}) REFERENCES T (Id)",
                _ => $"CREATE INDEX {index} ON T ({column})",
            };
            c.ExecuteNonQuery();
        }

        string libred = TemporaryDatabase.CopyPath(start, "idxmap-drop-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
            switch (kind)
            {
                case "primary key": db.CreateIndex("T", index, [(column, false)], isUnique: true, isPrimary: true); break;
                case "foreign key": db.AddForeignKey("T", new RelationshipSpec(index, "P", [(column, "Id")], IsEnforced: true, CascadeUpdate: false, CascadeDelete: false)); break;
                case "self-referencing foreign key": db.AddForeignKey("T", new RelationshipSpec(index, "T", [(column, "Id")], IsEnforced: true, CascadeUpdate: false, CascadeDelete: false)); break;
                default: db.CreateIndex("T", index, [(column, false)]); break;
            }

        output.WriteLine(label);
        string difference = DropTableParityAccessTests.Difference(ace, libred);
        output.WriteLine(difference);
        Assert.Equal("", difference);
    }
}