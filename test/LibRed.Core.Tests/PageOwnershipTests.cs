using LibRed.Catalog;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// No page belongs to two owners, and no owned page is free in the global free map, through the workloads that drive
/// the allocator's paths: shared reservations, owner groups, map growth, long values freed at once, an index built
/// over existing rows, and two connections writing in turn (<see cref="TestDatabases.AssertPageOwnership"/>). Checked
/// as the workload goes, not only at its end: a page handed out twice can be reused legitimately later, which hides it.
/// </summary>
public class PageOwnershipTests : TempDatabaseTest
{
    private static string Fresh(string prefix) => TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, prefix);

    // A 255-column table fills a page a row, so its data owner reaches eight pages and takes owner groups while its
    // primary key splits from the shared reservation — the pair that once handed one page to both.
    [Fact]
    public void A_wide_table_fill_keeps_every_page_to_one_owner()
    {
        string path = Fresh("own-wide-");
        using var db = JetDatabase.Open(path, readOnly: false);
        var columns = new List<ColumnSpec> { new("Id", JetDataType.Int32, 4, IsFixedLength: true) };
        for (int i = 1; i < 255; i++) columns.Add(new ColumnSpec($"c{i}", JetDataType.Currency, 8, IsFixedLength: true));
        db.CreateTable("W", columns, primaryKey: ["Id"]);
        Table table = db.OpenTable("W");
        var row = new object?[255];
        for (int r = 0; r < 2_000; r++)
        {
            row[0] = r;
            for (int i = 1; i < 255; i++) row[i] = (decimal)i;
            table.Insert(row);
            if (r % 50 == 0) TestDatabases.AssertPageOwnership(db);
        }
        TestDatabases.AssertPageOwnership(db);
    }

    // Long values through every route: inline, single pages, chains, an update freeing its old chain at once, a
    // delete releasing to close.
    [Fact]
    public void Long_value_churn_keeps_every_page_to_one_owner()
    {
        string path = Fresh("own-memo-");
        using var db = JetDatabase.Open(path, readOnly: false);
        db.CreateTable("M",
            [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new("Body", JetDataType.Memo, 0, IsFixedLength: false)],
            primaryKey: ["Id"]);
        Table table = db.OpenTable("M");
        for (int i = 0; i < 60; i++)
        {
            table.Insert([i, new string((char)('a' + i % 26), i % 3 == 0 ? 20_000 : 40 * i)]);
            TestDatabases.AssertPageOwnership(db);
        }
        foreach ((RowId id, object?[] values) in table.Rows().WithIds().Where((_, i) => i % 2 == 0).ToList())
        {
            values[1] = new string('z', (int)values[0]! % 4 == 0 ? 30_000 : 500);
            table.Update(id, values, new HashSet<int> { 1 });
            TestDatabases.AssertPageOwnership(db);
        }
        foreach ((RowId id, _) in table.Rows().WithIds().Where((_, i) => i % 3 == 0).ToList())
        {
            table.Delete(id);
            TestDatabases.AssertPageOwnership(db);
        }
    }

    [Fact]
    public void An_index_built_over_existing_rows_keeps_every_page_to_one_owner()
    {
        string path = Fresh("own-index-");
        using var db = JetDatabase.Open(path, readOnly: false);
        db.CreateTable("I",
            [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new("Name", JetDataType.Text, 200, IsFixedLength: false)]);
        Table table = db.OpenTable("I");
        for (int i = 0; i < 3_000; i++) table.Insert([i, $"name {i:D5} {new string('x', i % 90)}"]);
        TestDatabases.AssertPageOwnership(db);
        db.CreateIndex("I", "ixName", [("Name", false)]);
        TestDatabases.AssertPageOwnership(db);
        db.CreateIndex("I", "ixId", [("Id", true)], isUnique: true);
        TestDatabases.AssertPageOwnership(db);
    }

    // A connection's reservation sits past the end of the file while the other one grows it.
    [Fact]
    public void Two_connections_writing_in_turn_keep_every_page_to_one_owner()
    {
        string path = Fresh("own-two-");
        using (var setup = JetDatabase.Open(path, readOnly: false))
        {
            setup.CreateTable("A", [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new("Body", JetDataType.Memo, 0, IsFixedLength: false)]);
            setup.CreateTable("B", [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new("Body", JetDataType.Memo, 0, IsFixedLength: false)]);
        }
        using var first = JetDatabase.Open(path, readOnly: false);
        using var second = JetDatabase.Open(path, readOnly: false);
        for (int i = 0; i < 40; i++)
        {
            first.OpenTable("A").Insert([i, new string('a', 6_000)]);
            TestDatabases.AssertPageOwnership(first);
            second.OpenTable("B").Insert([i, new string('b', 6_000)]);
            TestDatabases.AssertPageOwnership(second);
        }
    }

    // The check itself: an owned page marked free in the global free map is reported.
    [Fact]
    public void An_owned_page_marked_free_is_reported()
    {
        string path = Fresh("own-planted-");
        using var db = JetDatabase.Open(path, readOnly: false);
        Table table = db.OpenTable("Customers");
        int owned = new UsageMap(table.Channel, table.Definition).DataPages().First();
        (int row, int holderPage, byte[] holder, _) = TestDatabases.GlobalMap(table.Channel, db.Format.FreePagesMapPointerOffset);
        TestDatabases.SetMapBit(holder, db.Format, row, owned, set: true);
        table.Channel.WritePage(holderPage, holder);

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => TestDatabases.AssertPageOwnership(db));
    }
}
