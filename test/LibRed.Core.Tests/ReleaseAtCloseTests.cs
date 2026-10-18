using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// ACE holds the pages a session frees — a deleted row's packed long value, a dropped index, a dropped table — until
// the session closes, then returns them and anything in the global released-pages map to the global free map and
// clears the released map, lengthening it to cover the highest page released (docs/format/page-05-usage-maps.md
// §9.1). Two kinds are freed sooner: a chained long value an UPDATE replaces or a DELETE removes is free at once, and
// a rewritten definition's old continuation pages are free when the statement commits. Both maps are found through
// page 0's pointers; Northwind's free map has 310 and 329 inside the file, and its released map is empty.
public class ReleaseAtCloseTests
{
    private static readonly JetFormatBase Format = TestDatabases.FormatOf(TestDatabases.NorthwindAccdb);

    [Fact]
    public void Released_pages_stay_unreusable_until_close_then_become_free()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-close-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                var allocator = channel.Allocator;
                allocator.Release(300);
                allocator.Release(301);

                Assert.False(FreeMapBit(channel,300));
                Assert.False(FreeMapBit(channel,301));
                Assert.Equal(310, allocator.Allocate());   // the free pages, never the released ones
                Assert.Equal(329, allocator.Allocate());
                Assert.Equal(353, allocator.Allocate());
            }

            using (var db = JetDatabase.Open(path))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                Assert.True(FreeMapBit(channel,300));
                Assert.True(FreeMapBit(channel,301));
                Assert.False(FreeMapBit(channel,310));
                Assert.All(ReleasedBits(channel), b => Assert.Equal(0, b));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_rolled_back_release_frees_nothing_and_a_savepoint_rollback_keeps_the_earlier_ones()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-rollback-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                var allocator = channel.Allocator;

                channel.BeginTransaction();
                allocator.Release(300);
                channel.RollbackTransaction();

                channel.BeginTransaction();
                allocator.Release(301);
                Savepoint savepoint = channel.CreateSavepoint();
                allocator.Release(302);
                channel.RollbackToSavepoint(savepoint);
                channel.CommitTransaction();

                channel.BeginTransaction();
                allocator.Release(303);
                // left open: the close discards it
            }

            using (var db = JetDatabase.Open(path))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                Assert.False(FreeMapBit(channel,300));
                Assert.True(FreeMapBit(channel,301));
                Assert.False(FreeMapBit(channel,302));
                Assert.False(FreeMapBit(channel,303));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void The_close_lengthens_the_released_map_to_cover_the_highest_page_released()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-length-");
        try
        {
            int before, highest;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                var allocator = channel.Allocator;
                before = ReleasedBits(channel).Length;
                for (int i = 0; i < 400; i++) allocator.Allocate();
                highest = channel.PageCount - 1;
                allocator.Release(highest);
            }

            using (var db = JetDatabase.Open(path))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                // 5-byte header, then the bitmap to the highest page in whole 4-byte words.
                int growth = Format.UsageMapInlineGrowthSize;
                int expected = (BitmapBits.ByteCount(highest + 1) + growth - 1) / growth * growth;
                Assert.True(expected > before);
                Assert.Equal(expected, ReleasedBits(channel).Length);
                Assert.All(ReleasedBits(channel), b => Assert.Equal(0, b));
                Assert.True(FreeMapBit(channel,highest));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Pages_already_in_the_released_map_are_merged_by_a_close_that_wrote_but_not_by_an_idle_one()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-merge-");
        try
        {
            int holderPage;
            byte[] holder;
            using (var channel = PageChannel.Open(path, readOnly: true))
            {
                (_, holderPage, holder, DataPage.RowSlot slot) = TestDatabases.GlobalMap(channel, Format.ReleasedPagesMapPointerOffset);
                Span<byte> record = InlineMap(holder, slot);
                int bit = 300 - UsageMap.StartPage(record, Format);
                Span<byte> bits = UsageMap.InlineBits(record, Format);
                Assert.InRange(bit, 0, bits.Length * 8 - 1);
                BitmapBits.Set(bits, bit, true);
            }
            TestDatabases.WritePage(path, holderPage, holder);

            using (JetDatabase.Open(path, readOnly: false)) { }
            Assert.Equal(holder, TestDatabases.ReadPage(path, holderPage));   // an idle writable close changes nothing

            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("Wrote", [new("Id", JetDataType.Int32, 4, IsFixedLength: true)]);

            using (var db = JetDatabase.Open(path))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                Assert.True(FreeMapBit(channel,300));
                Assert.All(ReleasedBits(channel), b => Assert.Equal(0, b));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Dropping_an_index_holds_its_root_but_a_memo_update_frees_the_old_chain_at_once()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-paths-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("Paths",
                [new("Id", JetDataType.Int32, 4, IsFixedLength: true),
                 new("M", JetDataType.Memo, 0, IsFixedLength: false)],
                primaryKey: ["Id"]);
            Table table = db.OpenTable("Paths");
            table.Insert([1, new string('a', 20000)]);
            PageChannel channel = table.Channel;

            int root = db.Catalog.FindTable("Paths")!.Indexes.Single().RootPage;
            db.DropIndex("Paths", db.Catalog.FindTable("Paths")!.Indexes.Single().Name);
            Assert.False(FreeMapBit(channel,root));

            table = db.OpenTable("Paths");
            (RowId id, object?[] values) = table.Rows().WithIds().Single();
            // The old chain is freed after the new value is written, as ACE does, so this update grows the file...
            int pagesBefore = channel.PageCount;
            values[1] = new string('b', 20000);
            table.Update(id, values, new HashSet<int> { 1 });
            Assert.True(channel.PageCount > pagesBefore);

            // ...but it is free at once, not held to close: the next update's chain starts on the old chain's first page,
            // 354, and then continues through the session's shared reservation, growing the file as well — measured
            // through ACE on this sequence: 375 pages after the first update, 384 after the second, the chain on 354
            // and 375-383.
            Assert.Equal(375, channel.PageCount);
            values[1] = new string('c', 20000);
            table.Update(id, values, new HashSet<int> { 1 });
            Assert.Equal(384, channel.PageCount);
            (int Row, int Page) chain = Assert.Single(table.Definition.LongValueOwnedMaps.Values);
            Assert.Contains(354, new UsageMap(channel, table.Definition).PagesInMap(chain.Row, chain.Page));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_deleted_rows_chained_long_value_is_free_at_once_and_a_packed_one_is_held()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-delete-");
        try
        {
            int packedPage;
            int[] chain;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Doc",
                    [new("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new("M", JetDataType.Memo, 0, IsFixedLength: false)]);
                Table table = db.OpenTable("Doc");
                PageChannel channel = table.Channel;
                table.Insert([1, new string('a', 20000)]);
                (int Row, int Page) owned = Assert.Single(table.Definition.LongValueOwnedMaps.Values);
                chain = [.. new UsageMap(channel, table.Definition).PagesInMap(owned.Row, owned.Page)];
                table.Insert([2, new string('b', 1000)]);
                packedPage = new UsageMap(channel, table.Definition).PagesInMap(owned.Row, owned.Page).Except(chain).Single();

                List<(RowId Id, object?[] Values)> rows = [.. table.Rows().WithIds()];
                table.Delete(rows.Single(r => (int)r.Values[0]! == 1).Id);
                Assert.All(chain, page => Assert.True(FreeMapBit(channel, page)));

                table.Delete(rows.Single(r => (int)r.Values[0]! == 2).Id);
                Assert.False(FreeMapBit(channel, packedPage));
            }

            using (var db = JetDatabase.Open(path))
                Assert.True(FreeMapBit(db.OpenTable("MSysObjects").Channel, packedPage));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // An UPDATE replacing a chained value with one that does not chain takes the long-value page the next request
    // would get, before writing anything, and frees it unwritten with the old chain; an UPDATE emptying a packed
    // value's page holds it, as a delete does. Measured through ACE: on one connection, after a 5000-character value
    // went to pages 45-47 of a 48-page file, setting it to 'small' freed 45-48 and left the file at 48 pages.
    [Fact]
    public void Replacing_a_chained_value_frees_an_unwritten_page_and_emptying_a_packed_page_holds_it()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-update-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("Doc",
                [new("Id", JetDataType.Int32, 4, IsFixedLength: true),
                 new("M", JetDataType.Memo, 0, IsFixedLength: false)]);
            Table table = db.OpenTable("Doc");
            PageChannel channel = table.Channel;
            (int Row, int Page) Owned() => Assert.Single(db.OpenTable("Doc").Definition.LongValueOwnedMaps.Values);

            table.Insert([1, new string('a', 5000)]);
            int[] chain = [.. new UsageMap(channel, table.Definition).PagesInMap(Owned().Row, Owned().Page)];
            int pages = channel.PageCount;
            (RowId id, object?[] values) = table.Rows().WithIds().Single();
            values[1] = "small";
            table.Update(id, values, new HashSet<int> { 1 });
            Assert.Equal(pages, channel.PageCount);                    // the page taken is not written
            Assert.All(chain, page => Assert.True(FreeMapBit(channel, page)));
            Assert.True(FreeMapBit(channel, pages));                   // the next page, taken and freed unwritten

            table.Insert([2, new string('b', 1000)]);
            int packed = new UsageMap(channel, table.Definition).PagesInMap(Owned().Row, Owned().Page).Except(chain).Single();
            (id, values) = table.Rows().WithIds().Single(r => (int)r.Values[0]! == 2);
            values[1] = null;
            table.Update(id, values, new HashSet<int> { 1 });
            Assert.False(FreeMapBit(channel, packed));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_rewritten_definitions_old_continuation_pages_are_free_when_the_statement_commits()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-rewrite-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var specs = new List<ColumnSpec> { new("Id", JetDataType.Int32, 4, IsFixedLength: true) };
            for (int i = 1; i <= 252; i++) specs.Add(new($"C{i:D3}", JetDataType.Text, 40, IsFixedLength: false));
            db.CreateTable("Wide", specs);
            PageChannel channel = db.OpenTable("Wide").Channel;
            int[] Continuations() =>
                [.. TableDefinition.ReadChain(channel, db.Catalog.FindTable("Wide")!.DefinitionPage).ContinuationPages];

            // Rolled back, the rewrite frees nothing.
            int[] original = Continuations();
            Assert.NotEmpty(original);
            db.BeginTransaction();
            Assert.True(db.DropColumn("Wide", "C005"));
            db.Rollback();
            Assert.Equal(original, Continuations());
            Assert.All(original, page => Assert.False(FreeMapBit(channel, page)));

            // Committed, the old continuations are held through the statement and free once it commits.
            db.BeginTransaction();
            Assert.True(db.DropColumn("Wide", "C005"));
            Assert.All(original, page => Assert.False(FreeMapBit(channel, page)));
            Assert.Empty(Continuations().Intersect(original));
            db.Commit(flush: false);
            Assert.All(original, page => Assert.True(FreeMapBit(channel, page)));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A global map's record on its holder page, which these tests expect in inline form.</summary>
    private static Span<byte> InlineMap(byte[] holder, DataPage.RowSlot slot)
    {
        Span<byte> record = holder.AsSpan(slot.Offset, slot.Length);
        Assert.Equal(UsageMapType.Inline, UsageMap.RecordType(record));
        return record;
    }

    private static bool FreeMapBit(PageChannel channel, int page)
    {
        (_, _, byte[] holder, DataPage.RowSlot slot) = TestDatabases.GlobalMap(channel, Format.FreePagesMapPointerOffset);
        Span<byte> record = InlineMap(holder, slot);
        int bit = page - UsageMap.StartPage(record, Format);
        Span<byte> bits = UsageMap.InlineBits(record, Format);
        if (bit < 0 || bit / 8 >= bits.Length) return false;
        return BitmapBits.Get(bits, bit);
    }

    private static byte[] ReleasedBits(PageChannel channel)
    {
        (_, _, byte[] holder, DataPage.RowSlot slot) = TestDatabases.GlobalMap(channel, Format.ReleasedPagesMapPointerOffset);
        return UsageMap.InlineBits(InlineMap(holder, slot), Format).ToArray();
    }
}