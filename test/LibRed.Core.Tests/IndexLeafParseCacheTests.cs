using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A leaf rewritten by an insert hands the channel the parse it was built from, so the next insert into the leaf
/// does not decode it again. That is only sound while the parse is exactly what decoding the written bytes gives,
/// and nothing else would notice if it drifted: every later insert and seek would simply work from the stale
/// entries. So after every insert, every cached parse of the table's index pages is checked against a fresh decode.
/// </summary>
public class IndexLeafParseCacheTests
{
    private const int Rows = 1_500;

    private static (JetDatabase Db, string Path) Fresh(ColumnSpec key)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "leafparse-");
        var db = JetDatabase.Open(path, readOnly: false);
        db.CreateTable("T", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true), key]);
        db.CreateIndex("T", "PK_T", [("Id", false)], isUnique: true, isPrimary: true);
        db.CreateIndex("T", "IX_K", [("K", false)]);
        return (db, path);
    }

    private static void Insert(JetDatabase db, int id, object key)
    {
        Table table = db.OpenTable("T");
        var values = new object?[table.Definition.Columns.Count];
        values[table.Definition.FindColumn("Id")!.Index] = id;
        values[table.Definition.FindColumn("K")!.Index] = key;
        table.Insert(values);
    }

    /// <summary>Checks every cached parse of the table's index pages, returning how many there were.</summary>
    private static int AssertCachedParsesMatch(JetDatabase db)
    {
        TableDef table = db.OpenTable("T").Definition;
        var writer = new IndexWriter(db.Channel, table);
        int cached = 0;
        for (int page = 0; page < db.Channel.PageCount; page++)
        {
            if (writer.CachedParseMatchesPage(page) is not { } matches) continue;
            Assert.True(matches, $"The cached parse of index page {page} differs from its bytes.");
            cached++;
        }
        return cached;
    }

    // Integer keys arrive in order, so leaves fill, split at the right edge and grow the tree; text keys share a
    // long prefix, so leaves also compress in place. Each arrives both outside a transaction and inside one,
    // where the parse lives beside the overlay instead of in the shared cache.
    public static TheoryData<bool, bool> Shapes => new()
    {
        { false, false }, { false, true }, { true, false }, { true, true },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Every_cached_leaf_parse_matches_its_bytes(bool textKeys, bool inTransaction)
    {
        var (db, path) = Fresh(textKeys
            ? new ColumnSpec("K", JetDataType.Text, 120, IsFixedLength: false)
            : new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true));
        try
        {
            if (inTransaction) db.BeginTransaction();
            int engaged = 0;
            for (int i = 0; i < Rows; i++)
            {
                // Duplicates in IX_K too: every third key repeats, so equal keys sit side by side on a leaf.
                int k = i - i % 3;
                Insert(db, i, textKeys ? $"a shared prefix long enough to compress - {k:D6}" : k);
                engaged += AssertCachedParsesMatch(db);
            }
            if (inTransaction) db.Commit(flush: false);
            AssertCachedParsesMatch(db);

            // Vacuous unless the written leaves' parses really were kept.
            Assert.True(engaged > Rows, $"Only {engaged} cached index parses were seen across {Rows} inserts.");
        }
        finally
        {
            db.Dispose();
            File.Delete(path);
        }
    }

    [Fact]
    public void A_savepoint_rollback_drops_the_parses_of_the_pages_it_restores()
    {
        var (db, path) = Fresh(new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true));
        try
        {
            db.BeginTransaction();
            for (int i = 0; i < 300; i++) Insert(db, i, i);

            // The rolled-back inserts rewrite the same leaves again; their parses must go with their bytes.
            var savepoint = db.CreateSavepoint();
            for (int i = 300; i < 600; i++) Insert(db, i, i);
            db.RollbackToSavepoint(savepoint);
            AssertCachedParsesMatch(db);

            for (int i = 600; i < 900; i++)
            {
                Insert(db, i, i);
                AssertCachedParsesMatch(db);
            }
            db.Commit(flush: false);
            AssertCachedParsesMatch(db);
        }
        finally
        {
            db.Dispose();
            File.Delete(path);
        }
    }
}
