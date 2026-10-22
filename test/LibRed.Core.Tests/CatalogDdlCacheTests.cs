using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class CatalogDdlCacheTests
{
    [Fact]
    public void An_empty_relationship_catalog_detects_its_first_row_and_rollback()
    {
        string path = TemporaryDatabase.CreatePath("ddl-cache-empty-");
        try
        {
            JetDatabase.Create(path);
            using var writer = JetDatabase.Open(path, readOnly: false);
            writer.CreateTable("P", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
            writer.CreateTable("C", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)]);
            using var reader = JetDatabase.Open(path);
            Assert.Empty(new UsageMap(reader.Channel, reader.Catalog.RequireTable("MSysRelationships")).DataPages());
            Assert.Empty(reader.Catalog.ForeignKeysOf("C"));
            Assert.Empty(reader.Catalog.ReferencesTo("P"));

            var relationship = new RelationshipSpec("FK_PC", "P", [("Id", "Id")], true,
                CascadeUpdate: false, CascadeDelete: false);
            writer.BeginTransaction();
            writer.AddForeignKey("C", relationship);
            Assert.Single(writer.Catalog.ForeignKeysOf("C"));
            Assert.Empty(reader.Catalog.ForeignKeysOf("C"));
            writer.Rollback();
            Assert.Empty(writer.Catalog.ForeignKeysOf("C"));
            Assert.Empty(reader.Catalog.ForeignKeysOf("C"));

            writer.AddForeignKey("C", relationship);
            Assert.Single(reader.Catalog.ForeignKeysOf("C"));
            Assert.Single(reader.Catalog.ReferencesTo("P"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Row_writes_preserve_ddl_and_refresh_requested_state(bool wide)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "ddl-cache-data-");
        try
        {
            using var writer = JetDatabase.Open(path, readOnly: false);
            var columns = new List<ColumnSpec>
            {
                new("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true),
                new("V", JetDataType.Int32, 4, IsFixedLength: true)
            };
            if (wide)
                for (int i = 0; i < 80; i++)
                    columns.Add(new ColumnSpec($"Column_{i:D3}_" + new string('x', 45), JetDataType.Text, 20, IsFixedLength: false));
            writer.CreateTable("T", columns, primaryKey: ["Id"]);
            using var reader = JetDatabase.Open(path);
            TableDefinition before = reader.Catalog.RequireTable("T");
            Assert.Equal(wide, before.NextDefinitionPage != 0);
            var ddl = before.Metadata;
            Assert.NotNull(ddl);
            Assert.Contains("T", reader.Catalog.Tables.Select(t => t.Name));

            object?[] values = new object?[columns.Count];
            values[1] = 7;
            writer.OpenTable("T").Insert(values);
            TableDefinition after = reader.Catalog.RequireTable("T");
            Assert.Same(ddl, after.Metadata);
            Assert.Equal(1, after.RowCount);
            Assert.Equal(2, after.RequireColumn("Id").Seed);
            Assert.Equal(1, after.Indexes.Single().UniqueEntryCount);
            Assert.Equal(0, before.RowCount);
            Assert.Equal(1, before.RequireColumn("Id").Seed);
            Assert.Equal(1, ddl.Columns[0].Seed);
            Assert.Equal(0, ddl.Indexes.Single().UniqueEntryCount);

            var table = writer.OpenTable("T");
            var row = table.Rows().WithIds().Single();
            row.Values[1] = 9;
            table.Update(row.Id, row.Values, new HashSet<int> { 1 });
            Assert.Same(ddl, reader.Catalog.RequireTable("T").Metadata);
            Assert.Equal(9, reader.OpenTable("T").Rows().Single()[1]);
            table.Delete(row.Id);
            Assert.Same(ddl, reader.Catalog.RequireTable("T").Metadata);
            Assert.Equal(0, reader.Catalog.RequireTable("T").RowCount);
            Assert.Same(ddl, reader.Catalog.Tables.Single(t => t.Name == "T").Metadata);

            var writerDdl = writer.Catalog.RequireTable("T").Metadata;
            writer.BeginTransaction();
            writer.OpenTable("T").Insert(new object?[columns.Count]);
            Assert.Same(writerDdl, writer.Catalog.RequireTable("T").Metadata);
            Assert.Equal(1, writer.Catalog.RequireTable("T").RowCount);
            Assert.Equal(0, reader.Catalog.RequireTable("T").RowCount);
            writer.Rollback();
            Assert.Same(writerDdl, writer.Catalog.RequireTable("T").Metadata);
            Assert.Same(ddl, reader.Catalog.RequireTable("T").Metadata);
            Assert.Equal(0, reader.Catalog.RequireTable("T").RowCount);
            Assert.Equal(3, reader.Catalog.RequireTable("T").RequireColumn("Id").Seed);

            writer.SetColumnDefault("T", "V", "42");
            TableDefinition withDefault = reader.Catalog.RequireTable("T");
            Assert.NotSame(ddl, withDefault.Metadata);
            Assert.Equal("42", withDefault.RequireColumn("V").DefaultValue);
            ddl = withDefault.Metadata;

            writer.CreateTable("Unrelated", [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true)]);
            Assert.Same(ddl, reader.Catalog.RequireTable("T").Metadata);
            Assert.Contains("Unrelated", reader.Catalog.Tables.Select(t => t.Name));
            writer.AddColumn("T", new ColumnSpec("Added", JetDataType.Int32, 4, IsFixedLength: true));
            Assert.NotSame(ddl, reader.Catalog.RequireTable("T").Metadata);
            Assert.NotNull(reader.Catalog.RequireTable("T").FindColumn("Added"));
            Assert.Null(before.FindColumn("Added"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Relationships_survive_data_writes_and_follow_ddl_and_rollback()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "ddl-cache-rel-");
        try
        {
            using var writer = JetDatabase.Open(path, readOnly: false);
            writer.CreateTable("P", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
            writer.CreateTable("C", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
            using var reader = JetDatabase.Open(path);
            Assert.Empty(reader.Catalog.ForeignKeysOf("C"));
            Assert.Empty(reader.Catalog.ReferencesTo("P"));
            var spec = new RelationshipSpec("FK_PC", "P", [("Id", "Id")], true, CascadeUpdate: true, CascadeDelete: true);
            writer.AddForeignKey("C", spec);
            ForeignKey fk = reader.Catalog.ForeignKeysOf("C").Single();
            Assert.True(fk.CascadeDelete);
            Assert.Equal("C", reader.Catalog.ReferencesTo("P").Single().Table);

            // Re-reading identical raw catalog pages is not a DDL change, whether they were evicted or
            // refreshed after an external writer. Preserve the decoded relationship object across it.
            var images = new Dictionary<int, ReadOnlyMemory<byte>>();
            _ = new UsageMap(writer.Channel, writer.Catalog.RequireTable("MSysRelationships")).DataPages(images).ToArray();
            PageCache cache = PageCache.Acquire(path);
            try
            {
                foreach (int page in images.Keys) cache.Remove(page);
            }
            finally { PageCache.Release(path); }
            Assert.Same(fk, reader.Catalog.ForeignKeysOf("C").Single());

            // The owned and free maps share a holder. Changing free-space bookkeeping must not discard
            // relationship rows when neither the owned-page set nor those rows changed.
            TableDefinition relationships = writer.Catalog.RequireTable("MSysRelationships");
            var maps = new UsageMap(writer.Channel, relationships);
            int dataPage = maps.DataPages().First();
            bool wasFree = maps.FreeDataPages().Contains(dataPage);
            var freeMap = writer.Channel.ReadPageShared(relationships.DefinitionPage)
                .ReadRecordPointer(writer.Channel.Format.TdefFreePagesOffset);
            maps.SetBit(freeMap.Row, freeMap.Page, dataPage, !wasFree);
            Assert.Same(fk, reader.Catalog.ForeignKeysOf("C").Single());
            maps.SetBit(freeMap.Row, freeMap.Page, dataPage, wasFree);
            Assert.Same(fk, reader.Catalog.ForeignKeysOf("C").Single());

            writer.OpenTable("P").Insert([1]);
            writer.OpenTable("C").Insert([1]);
            Assert.Same(fk, reader.Catalog.ForeignKeysOf("C").Single());
            writer.OpenTable("C").Delete(writer.OpenTable("C").Rows().WithIds().Single().Id);
            Assert.Same(fk, reader.Catalog.ForeignKeysOf("C").Single());

            // Updating an existing entry at capacity must not flush unrelated relationship sets.
            IEnumerable<ForeignKey>? unused = null;
            for (int i = 0; i < 62; i++)
            {
                IEnumerable<ForeignKey> empty = reader.Catalog.ForeignKeysOf($"Unused{i}");
                if (i == 0) unused = empty;
            }
            writer.SetColumnDefault("C", "Id", "0");
            fk = reader.Catalog.ForeignKeysOf("C").Single();
            Assert.Same(unused, reader.Catalog.ForeignKeysOf("Unused0"));
            writer.CreateTable("Unrelated", [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true)]);
            Assert.Same(fk, reader.Catalog.ForeignKeysOf("C").Single());

            Assert.True(writer.DropConstraint("C", "FK_PC"));
            Assert.Empty(reader.Catalog.ForeignKeysOf("C"));
            Assert.Empty(reader.Catalog.ReferencesTo("P"));
            writer.BeginTransaction();
            writer.AddForeignKey("C", spec);
            Assert.Single(writer.Catalog.ForeignKeysOf("C"));
            Assert.Empty(reader.Catalog.ForeignKeysOf("C"));
            writer.Rollback();
            Assert.Empty(writer.Catalog.ForeignKeysOf("C"));
            Assert.Empty(reader.Catalog.ForeignKeysOf("C"));

            // A new key at capacity evicts one entry, rather than flushing every decoded result.
            using var bounded = JetDatabase.Open(path);
            for (int i = 0; i < 64; i++) _ = bounded.Catalog.ForeignKeysOf($"Absent{i}");
            IEnumerable<ForeignKey> retained = bounded.Catalog.ForeignKeysOf("Absent63");
            Assert.Empty(bounded.Catalog.ForeignKeysOf("Absent64"));
            Assert.Same(retained, bounded.Catalog.ForeignKeysOf("Absent63"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_schema_edit_without_a_timestamp_or_notification_is_detected()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "ddl-cache-raw-");
        try
        {
            using var reader = JetDatabase.Open(path);
            TableDefinition before = reader.Catalog.RequireTable("Orders");
            Assert.True(before.RequireColumn("OrderID").IsUpdatable);
            using var channel = PageChannel.Open(path, readOnly: false);
            byte[] page = channel.ReadPage(before.DefinitionPage).Span.ToArray();
            int descriptors = TableDefinition.Regions.Of(page, channel.Format).ColumnDescriptors;
            page[descriptors + channel.Format.ColumnFlagsOffset] &= unchecked((byte)~(byte)ColumnFlags.Updatable);
            channel.WritePage(before.DefinitionPage, page);
            TableDefinition after = reader.Catalog.RequireTable("Orders");
            Assert.NotSame(before.Metadata, after.Metadata);
            Assert.False(after.RequireColumn("OrderID").IsUpdatable);
            Assert.True(before.RequireColumn("OrderID").IsUpdatable);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_continuation_edit_without_a_root_write_is_detected()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "ddl-cache-chain-");
        try
        {
            using var writer = JetDatabase.Open(path, readOnly: false);
            writer.CreateTable("Wide", Enumerable.Range(0, 200)
                .Select(i => new ColumnSpec($"C{i}", JetDataType.Text, 2, IsFixedLength: false)).ToArray());
            using var reader = JetDatabase.Open(path);
            TableDefinition before = reader.Catalog.RequireTable("Wide");
            Assert.True(before.RequireColumn("C199").IsUpdatable);
            using var channel = PageChannel.Open(path, readOnly: false);
            var chain = TableDefinition.ReadChain(channel, before.DefinitionPage);
            int position = TableDefinition.Regions.Of(chain.Buffer.Span, channel.Format).ColumnDescriptors
                + 199 * channel.Format.ColumnDescriptorSize + channel.Format.ColumnFlagsOffset;
            Assert.True(position >= channel.PageSize);
            int relative = position - channel.PageSize;
            int bodySize = channel.PageSize - channel.Format.TdefContinuationHeaderSize;
            int pageNumber = chain.ContinuationPages[relative / bodySize];
            byte[] page = channel.ReadPage(pageNumber).Span.ToArray();
            page[channel.Format.TdefContinuationHeaderSize + relative % bodySize]
                &= unchecked((byte)~(byte)ColumnFlags.Updatable);
            ReadOnlyMemory<byte> root = channel.ReadPageShared(before.DefinitionPage).Data;
            channel.WritePage(pageNumber, page);
            Assert.True(root.Equals(channel.ReadPageShared(before.DefinitionPage).Data));
            TableDefinition after = reader.Catalog.RequireTable("Wide");
            Assert.NotSame(before.Metadata, after.Metadata);
            Assert.False(after.RequireColumn("C199").IsUpdatable);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Revalidation_tracks_a_catalog_row_that_moves_to_another_page()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "ddl-cache-move-");
        try
        {
            using var writer = JetDatabase.Open(path, readOnly: false);
            using var reader = JetDatabase.Open(path);
            TableDefinition before = reader.Catalog.RequireTable("Orders");
            var objects = writer.OpenTable("MSysObjects");
            int name = objects.Definition.RequireColumn("Name").Index;
            int id = objects.Definition.RequireColumn("Id").Index;
            int type = objects.Definition.RequireColumn("Type").Index;
            int flags = objects.Definition.RequireColumn("Flags").Index;
            int properties = objects.Definition.RequireColumn("LvProp").Index;
            var original = objects.Rows().WithIds().Single(r => Equals(r.Values[name], "Orders"));
            objects.Delete(original.Id);
            for (int i = 0; i < 200; i++)
            {
                object?[] filler = (object?[])original.Values.Clone();
                filler[name] = $"F{i}";
                filler[id] = int.MinValue + 50_000 + i;
                filler[type] = (short)ObjectType.Container;
                filler[properties] = null;
                objects.Insert(filler);
            }
            objects.Insert(original.Values);
            var moved = objects.Rows().WithIds().Single(r => Equals(r.Values[name], "Orders"));
            Assert.NotEqual(original.Id.Page, moved.Id.Page);
            Assert.Same(before.Metadata, reader.Catalog.RequireTable("Orders").Metadata);

            moved.Values[flags] = (int)moved.Values[flags]! | (int)ObjectAttributes.Hidden;
            objects.Update(moved.Id, moved.Values, new HashSet<int> { flags });
            Assert.True(reader.Catalog.RequireTable("Orders").IsSystem);
            Assert.False(before.IsSystem);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Refreshing_live_state_at_capacity_does_not_flush_other_ddl()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "ddl-cache-full-");
        try
        {
            using var writer = JetDatabase.Open(path, readOnly: false);
            for (int i = 0; i < 63; i++)
                writer.CreateTable($"Cache{i}", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)]);
            using var reader = JetDatabase.Open(path);
            // These 63 entries plus the bootstrap MSysObjects definition fill the existing 64-entry bound.
            TableDefinition[] before = Enumerable.Range(0, 63).Select(i => reader.Catalog.RequireTable($"Cache{i}")).ToArray();
            writer.OpenTable("Cache0").Insert([1]);
            Assert.Equal(1, reader.Catalog.RequireTable("Cache0").RowCount);
            for (int i = 0; i < before.Length; i++)
                Assert.Same(before[i].Metadata, reader.Catalog.RequireTable($"Cache{i}").Metadata);

            writer.CreateTable("Cache63", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)]);
            Assert.NotNull(reader.Catalog.FindTable("Cache63"));
            Assert.Same(before[62].Metadata, reader.Catalog.RequireTable("Cache62").Metadata);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
