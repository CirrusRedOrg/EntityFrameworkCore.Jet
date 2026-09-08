using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Building an index over a populated table must produce a B-tree holding every row exactly once, in key
/// order, across as many leaves as it takes.
/// </summary>
/// <remarks>
/// Written after an attempted rewrite of the back-fill produced a <b>corrupt</b> index — 500 rows became 500
/// entries covering only 442 distinct rows, two leaves holding overlapping content — and the 583 existing
/// index tests all passed anyway. They build over too few rows to span several leaves while also checking the
/// whole ordering, so nothing was watching the one property that matters. These are that guard, and they are
/// deliberately written against the observable tree (present, ordered, unique) rather than against page
/// layout, so they hold for any implementation of the fill.
/// </remarks>
public class IndexBuildOrderingTests
{
    private static (JetDatabase Db, string Path) Fresh(string prefix)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, prefix);
        return (JetDatabase.Open(path, readOnly: false), path);
    }

    private static void Seed(JetDatabase db, int rows, Func<int, object?> value, ColumnSpec? keyColumn = null)
    {
        db.CreateTable("T", [
            new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
            keyColumn ?? new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true)]);

        for (int i = 0; i < rows; i++)
        {
            // A fresh Table per insert: it snapshots the definition and usage map at construction, so reusing
            // one across a load would have it placing rows by an increasingly stale picture of the pages.
            Table table = db.OpenTable("T");
            var values = new object?[table.Definition.Columns.Count];
            values[table.Definition.FindColumn("Id")!.Index] = i;
            values[table.Definition.FindColumn("K")!.Index] = value(i);
            table.Insert(values);
        }

        // The Table (and its usage map) was resolved before these rows existed; drop the cached definition so
        // a later scan sees the pages they went onto.
        db.Catalog.Invalidate();
    }

    /// <summary>Every row id the index yields, in index order, via a fully open range seek.</summary>
    private static List<int> IndexOrder(JetDatabase db, string indexName)
    {
        Table table = db.OpenTable("T");
        IndexDef index = table.Definition.Indexes.First(i => i.Name == indexName);
        int idOrdinal = table.Definition.FindColumn("Id")!.Index;
        return [.. table.SeekRangeRows(index, null, null).Select(r => Convert.ToInt32(r[idOrdinal]))];
    }

    [Theory]
    // Sizes straddling the page boundaries: one leaf, several, and enough to grow interior levels.
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(500)]
    [InlineData(2_000)]
    [InlineData(20_000)]
    public void Every_row_appears_once_and_in_key_order(int rows)
    {
        var (db, path) = Fresh("ixorder-");
        try
        {
            // K descends as Id ascends, so index order is the exact reverse of insertion order — a build that
            // merely preserved scan order would satisfy a weaker check but fail this one.
            Seed(db, rows, i => rows - i);

            // Guard the premise first: the index is built from this scan, so if the scan itself does not see
            // each row exactly once, an index check tells you nothing about the index.
            Table scanned = db.OpenTable("T");
            int idOrdinal = scanned.Definition.FindColumn("Id")!.Index;
            List<int> byScan = [.. scanned.Rows().Select(r => Convert.ToInt32(r[idOrdinal]))];
            Assert.Equal(rows, byScan.Count);
            Assert.Equal(rows, byScan.Distinct().Count());

            db.CreateIndex("T", "IX_K", [("K", false)]);

            List<int> byIndex = IndexOrder(db, "IX_K");
            Assert.Equal(rows, byIndex.Count);
            Assert.Equal(rows, byIndex.Distinct().Count());   // the check that caught the corrupt build
            Assert.Equal(Enumerable.Range(0, rows).Reverse(), byIndex);
        }
        finally { db.Dispose(); TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Duplicate_keys_are_all_kept_and_grouped()
    {
        var (db, path) = Fresh("ixdup-");
        try
        {
            // 20 keys over 4,000 rows: 200 entries per key, so a key's run spans leaf boundaries.
            Seed(db, 4_000, i => i % 20);
            db.CreateIndex("T", "IX_K", [("K", false)]);

            List<int> byIndex = IndexOrder(db, "IX_K");
            Assert.Equal(4_000, byIndex.Count);
            Assert.Equal(4_000, byIndex.Distinct().Count());
            Assert.Equal(byIndex.OrderBy(id => id % 20).ThenBy(id => id), byIndex);
        }
        finally { db.Dispose(); TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_unique_index_rejects_a_duplicate()
    {
        var (db, path) = Fresh("ixuniq-");
        try
        {
            Seed(db, 500, i => i % 499); // exactly one collision
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => db.CreateIndex("T", "IX_K", [("K", false)], isUnique: true));
            Assert.Contains("duplicate key values exist", error.Message);
        }
        finally { db.Dispose(); TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_unique_index_allows_many_nulls()
    {
        var (db, path) = Fresh("ixuniqnull-");
        try
        {
            // Jet's uniqueness is over the non-null keys only, so null keys must not collide with each other
            // even though their encoded keys are identical.
            Seed(db, 300, i => i % 3 == 0 ? null : i);
            db.CreateIndex("T", "IX_K", [("K", false)], isUnique: true);

            Assert.Equal(300, IndexOrder(db, "IX_K").Count);
        }
        finally { db.Dispose(); TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Nulls_lead_the_index_and_ignore_null_leaves_them_out()
    {
        var (db, path) = Fresh("ixnull-");
        try
        {
            Seed(db, 600, i => i % 4 == 0 ? null : i);
            db.CreateIndex("T", "IX_K", [("K", false)]);
            db.CreateIndex("T", "IX_K_Ign", [("K", false)], ignoreNulls: true);

            List<int> all = IndexOrder(db, "IX_K");
            Assert.Equal(600, all.Count);
            Assert.All(all.Take(150), id => Assert.Equal(0, id % 4)); // null keys sort first (0x00 < 0x7F)

            Assert.Equal(450, IndexOrder(db, "IX_K_Ign").Count);      // and are absent from the IGNORE NULL one
        }
        finally { db.Dispose(); TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Wide_text_keys_span_many_leaves_in_order()
    {
        var (db, path) = Fresh("ixwide-");
        try
        {
            // ~190-character keys: a handful per page, so 3,000 rows build a deep tree. The zero-padded prefix
            // makes key order equal Id order, and the repeated tail gives the pages a long shared prefix —
            // which is what exercises the compression path (§10.3) rather than just the fill.
            // 400 bytes = 200 characters: the declared length is in BYTES, and these values are 187 chars.
            Seed(db, 3_000, i => $"{i:D6}-{new string((char)('a' + i % 26), 180)}",
                new ColumnSpec("K", JetDataType.Text, 400, IsFixedLength: false));
            db.CreateIndex("T", "IX_K", [("K", false)]);

            List<int> byIndex = IndexOrder(db, "IX_K");
            Assert.Equal(3_000, byIndex.Count);
            Assert.Equal(3_000, byIndex.Distinct().Count());
            Assert.Equal(Enumerable.Range(0, 3_000), byIndex);
        }
        finally { db.Dispose(); TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_composite_index_orders_by_both_columns()
    {
        var (db, path) = Fresh("ixcomp-");
        try
        {
            db.CreateTable("T", [
                new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                new ColumnSpec("A", JetDataType.Int32, 4, IsFixedLength: true),
                new ColumnSpec("B", JetDataType.Int32, 4, IsFixedLength: true)]);

            Table table = db.OpenTable("T");
            int idOrdinal = table.Definition.FindColumn("Id")!.Index;
            int aOrdinal = table.Definition.FindColumn("A")!.Index;
            int bOrdinal = table.Definition.FindColumn("B")!.Index;
            for (int i = 0; i < 1_000; i++)
            {
                var values = new object?[table.Definition.Columns.Count];
                values[idOrdinal] = i;
                values[aOrdinal] = i % 10;
                values[bOrdinal] = 999 - i;
                table.Insert(values);
            }

            db.CreateIndex("T", "IX_AB", [("A", false), ("B", false)]);

            Table reopened = db.OpenTable("T");
            IndexDef index = reopened.Definition.Indexes.First(i => i.Name == "IX_AB");
            var pairs = reopened.SeekRangeRows(index, null, null)
                .Select(r => (A: Convert.ToInt32(r[aOrdinal]), B: Convert.ToInt32(r[bOrdinal])))
                .ToList();

            Assert.Equal(1_000, pairs.Count);
            Assert.Equal(pairs.OrderBy(p => p.A).ThenBy(p => p.B), pairs);
        }
        finally { db.Dispose(); TemporaryDatabase.Delete(path); }
    }
}
