using System.Buffers.Binary;
using LibRed;
using LibRed.Catalog;
using LibRed.IO;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class TableCreatorTests
{
    [Fact]
    public void Multi_page_insert_marks_only_the_tail_page_free()
    {
        string path = CopyToTemp();
        ColumnSpec[] schema =
        [
            new("Id", JetDataType.Int32, 4, IsFixedLength: true),
            new("T", JetDataType.Text, 400, IsFixedLength: false),
        ];
        string pad = new string('y', 180);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("FM", schema, primaryKey: ["Id"]);
                var table = db.OpenTable("FM");
                for (int i = 1; i <= 200; i++) table.Insert([i, $"{i:D4}-{pad}"]);
            }

            using (var db = JetDatabase.Open(path))
            {
                var table = db.OpenTable("FM");
                var maps = new UsageMap(table.Channel, table.Definition);
                var owned = maps.DataPages().ToList();
                Assert.True(owned.Count > 1, "expected multiple owned data pages");

                // Access keeps only the current append tail (highest owned page) in the free-pages
                // map — earlier full pages are cleared as it moves past them. LibRed must match.
                var free = maps.FreeDataPages().ToList();
                Assert.Equal([owned.Max()], free);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Insert_maintains_the_unique_index_stat_and_leaves_total_at_zero()
    {
        string path = CopyToTemp();
        ColumnSpec[] schema = [new("Id", JetDataType.Int32, 4, IsFixedLength: true)];
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Stats", schema, primaryKey: ["Id"]);
                var table = db.OpenTable("Stats");
                table.Insert([10]);
                table.Insert([20]);
                table.Insert([30]);
            }

            using (var db = JetDatabase.Open(path))
            {
                var table = db.OpenTable("Stats");
                (int total, int unique) = LibRed.Catalog.TableDefinition.ReadIndexCounts(
                    table.Channel.ReadPage(table.Definition.DefinitionPage).Span, table.Channel.Format, ordinal: 0);

                // Access maintains the cumulative unique-entry count live (one distinct key per row
                // for a unique index) but leaves the total-entry count at 0 until compact.
                Assert.Equal(3, unique);
                Assert.Equal(0, total);
                Assert.Equal(3, table.Definition.Indexes.Single(i => i.IsPrimaryKey).UniqueEntryCount);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Insert_spanning_multiple_data_pages_round_trips()
    {
        string path = CopyToTemp();
        ColumnSpec[] schema =
        [
            new("Id", JetDataType.Int32, 4, IsFixedLength: true),
            new("T", JetDataType.Text, 400, IsFixedLength: false),
        ];
        const int n = 200;                 // ~180-byte rows → tens of rows per 4 KB page
        string pad = new string('x', 180);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Big", schema, primaryKey: ["Id"]);
                var table = db.OpenTable("Big");
                for (int i = 1; i <= n; i++) table.Insert([i, $"{i:D4}-{pad}"]);
            }

            using (var db = JetDatabase.Open(path))
            {
                var table = db.OpenTable("Big");
                var rows = table.Rows().OrderBy(r => (int)r[0]!).ToList();
                Assert.Equal(n, rows.Count);
                Assert.Equal(Enumerable.Range(1, n), rows.Select(r => (int)r[0]!));
                Assert.Equal($"0001-{pad}", rows[0][1]);

                // The rows must have spilled onto more than one owned data page (allocate-on-overflow).
                int ownedPages = new Storage.UsageMap(table.Channel, table.Definition).DataPages().Count();
                Assert.True(ownedPages > 1, $"expected multiple data pages, got {ownedPages}");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Autonumber_is_generated_when_the_column_is_omitted()
    {
        string path = CopyToTemp();
        ColumnSpec[] schema =
        [
            new("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true),
            new("V", JetDataType.Text, 40, IsFixedLength: false),
        ];
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Auto", schema, primaryKey: ["Id"]);
                var table = db.OpenTable("Auto");
                table.Insert([null, "a"]);   // Id omitted -> generated 1
                table.Insert([null, "b"]);   // -> 2
                table.Insert([7, "seven"]);  // explicit value jumps the high-water to 7 (Jet allows it)
                table.Insert([null, "c"]);   // -> 8 (continues from 7)
            }

            using (var db = JetDatabase.Open(path))
            {
                var byV = db.OpenTable("Auto").Rows()
                    .ToDictionary(r => (string)r[1]!, r => Convert.ToInt32(r[0]));
                Assert.Equal(1, byV["a"]);
                Assert.Equal(2, byV["b"]);
                Assert.Equal(7, byV["seven"]);
                Assert.Equal(8, byV["c"]);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Autonumber_insert_tracks_the_tdef_high_water_mark()
    {
        string path = CopyToTemp();
        ColumnSpec[] schema =
        [
            new("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true),
            new("V", JetDataType.Text, 40, IsFixedLength: false),
        ];
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Auto", schema, primaryKey: ["Id"]);
                var table = db.OpenTable("Auto");
                table.Insert([5, "five"]);   // out of order on purpose: the field is a high-water
                table.Insert([2, "two"]);    // mark (max assigned), not the last value written
            }

            using (var db = JetDatabase.Open(path))
            {
                var table = db.OpenTable("Auto");
                int highWater = BinaryPrimitives.ReadInt32LittleEndian(
                    table.Channel.ReadPage(table.Definition.DefinitionPage)
                        .Span.Slice(table.Channel.Format.TdefLastAutoNumberOffset, 4));
                Assert.Equal(5, highWater); // Access reads this to pick the next id (= 6)
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string CopyToTemp()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-create-");
        return path;
    }

    private static readonly ColumnSpec[] Schema =
    [
        new("Id", JetDataType.Int32, 4, IsFixedLength: true),
        new("Name", JetDataType.Text, 510, IsFixedLength: false),
        new("When", JetDataType.DateTime, 8, IsFixedLength: true),
    ];

    [Fact]
    public void Created_table_appears_in_the_catalog_with_its_columns()
    {
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("Widgets", Schema);

            using (var db = JetDatabase.Open(path))
            {
                var def = db.Catalog.FindTable("Widgets");
                Assert.NotNull(def);
                Assert.False(def!.IsSystem);
                Assert.Equal(["Id", "Name", "When"], def.Columns.Select(c => c.Name));
                Assert.Equal(
                    [JetDataType.Int32, JetDataType.Text, JetDataType.DateTime],
                    def.Columns.Select(c => c.Type));
                Assert.Empty(db.OpenTable("Widgets").Rows()); // starts empty
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Created_table_round_trips_inserted_rows()
    {
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Widgets", Schema);
                var table = db.OpenTable("Widgets");
                table.Insert([1, "first", new DateTime(2020, 1, 2)]);
                table.Insert([2, "second", new DateTime(2021, 3, 4)]);
            }

            using (var db = JetDatabase.Open(path))
            {
                var rows = db.OpenTable("Widgets").Rows()
                    .OrderBy(r => (int)r[0]!)
                    .ToList();

                Assert.Equal(2, rows.Count);
                Assert.Equal([1, "first", new DateTime(2020, 1, 2)], rows[0]);
                Assert.Equal([2, "second", new DateTime(2021, 3, 4)], rows[1]);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Created_table_round_trips_nulls_and_numeric_edges()
    {
        string path = CopyToTemp();
        ColumnSpec[] schema =
        [
            new("Id", JetDataType.Int32, 4, IsFixedLength: true),
            new("Small", JetDataType.Int16, 2, IsFixedLength: true),
            new("Tiny", JetDataType.Byte, 1, IsFixedLength: true),
            new("Money", JetDataType.Currency, 8, IsFixedLength: true),
            new("Flag", JetDataType.Boolean, 1, IsFixedLength: true),
            new("Label", JetDataType.Text, 40, IsFixedLength: false),
        ];

        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Edges", schema, primaryKey: ["Id"]);
                var table = db.OpenTable("Edges");
                table.Insert([int.MinValue, short.MinValue, byte.MinValue, -1234.5678m, false, "min"]);
                table.Insert([0, null, null, null, null, null]);
                table.Insert([int.MaxValue, short.MaxValue, byte.MaxValue, 999999.9999m, true, "max"]);
            }

            using (var db = JetDatabase.Open(path))
            {
                var table = db.OpenTable("Edges");
                var rows = table.Rows().OrderBy(r => Convert.ToInt32(r[0])).ToList();

                Assert.Equal(3, rows.Count);
                Assert.Equal([int.MinValue, short.MinValue, byte.MinValue, -1234.5678m, false, "min"], rows[0]);
                Assert.Equal([0, null, null, null, false, null], rows[1]); // Jet has no nullable BIT.
                Assert.Equal([int.MaxValue, short.MaxValue, byte.MaxValue, 999999.9999m, true, "max"], rows[2]);

                var pk = Assert.Single(table.Definition.Indexes, i => i.IsPrimaryKey);
                var ids = new IndexCursor(table.Channel, pk.RootPage)
                    .Entries(pk.Columns)
                    .Select(e => (int)e.Key[0]!)
                    .ToList();
                Assert.Equal([int.MinValue, 0, int.MaxValue], ids);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Created_table_with_primary_key_has_a_working_index()
    {
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Widgets", Schema, primaryKey: ["Id"]);
                var table = db.OpenTable("Widgets");
                // Insert out of order; the index must keep key order.
                table.Insert([3, "c", new DateTime(2022, 1, 1)]);
                table.Insert([1, "a", new DateTime(2020, 1, 1)]);
                table.Insert([2, "b", new DateTime(2021, 1, 1)]);
            }

            using (var db = JetDatabase.Open(path))
            {
                var def = db.Catalog.FindTable("Widgets")!;
                var pk = Assert.Single(def.Indexes, i => i.IsPrimaryKey);
                Assert.Equal("PrimaryKey", pk.Name);
                Assert.True(pk.IsUnique);
                Assert.Equal(["Id"], pk.Columns.Select(c => c.Column.Name));

                // The index B-tree returns the rows in key order, even though they were inserted out of order.
                var table = db.OpenTable("Widgets");
                var ids = new IndexCursor(table.Channel, pk.RootPage)
                    .Entries(pk.Columns)
                    .Select(e => (int)e.Key[0]!)
                    .ToList();
                Assert.Equal([1, 2, 3], ids);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Creating_a_table_leaves_existing_tables_readable()
    {
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("Widgets", Schema);

            using (var db = JetDatabase.Open(path))
            {
                // The pre-existing Northwind data is untouched.
                Assert.Equal(3, db.OpenTable("Shippers").Rows().Count());
                Assert.Equal(830, db.OpenTable("Orders").Rows().Count());
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Every JetDatabase DDL entry point takes a raw ColumnSpec and hands it to the writer, so the version gate
    // has to be here and not only on the SQL path above it — otherwise a Core caller writes a BIGINT
    // descriptor into an ACE 12 file, a column Access cannot read.
    [Fact]
    public void Creating_a_table_refuses_a_type_the_file_is_too_old_for()
    {
        string path = TemporaryDatabase.CreatePath("versiongate-");
        try
        {
            JetDatabase.Create(path, version: 0x02);        // ACE 12
            using var db = JetDatabase.Open(path, readOnly: false);

            var error = Assert.Throws<NotSupportedException>(() => db.CreateTable("T",
                [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                 new ColumnSpec("Big", JetDataType.Int64, 8, IsFixedLength: false)]));
            Assert.Contains("Access 2016", error.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Every lookup downstream resolves an index by name with First/FirstOrDefault, so two blocks sharing one
    // name make DROP INDEX remove an arbitrary one. ACE refuses the duplicate outright.
    [Fact]
    public void A_duplicate_index_name_is_refused()
    {
        string path = TemporaryDatabase.CreatePath("dupindex-");
        try
        {
            JetDatabase.Create(path);
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("T", [Long("K"), Long("A"), Long("B")]);

            db.CreateIndex("T", "IX", [("A", false)], isUnique: false, isPrimary: false,
                disallowNull: false, ignoreNulls: false);
            Assert.Throws<InvalidOperationException>(() => db.CreateIndex("T", "IX", [("B", false)],
                isUnique: false, isPrimary: false, disallowNull: false, ignoreNulls: false));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The TDEF header holds a single seed/increment pair, so a second AutoNumber column would carry the 0x04
    // flag with no counter of its own. ALTER's promote path always refused it; CREATE silently took the first
    // and ignored the rest, and ADD COLUMN checked nothing.
    [Fact]
    public void A_table_may_only_have_one_autonumber_column()
    {
        string path = TemporaryDatabase.CreatePath("twocounters-");
        try
        {
            JetDatabase.Create(path);
            using var db = JetDatabase.Open(path, readOnly: false);

            Assert.Throws<NotSupportedException>(() => db.CreateTable("T", [Counter("A"), Counter("B")]));

            db.CreateTable("U", [Counter("A"), Long("K")]);
            Assert.Throws<NotSupportedException>(() => db.AddColumn("U", Counter("C")));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static ColumnSpec Long(string name) => new(name, JetDataType.Int32, 4, IsFixedLength: true);
    private static ColumnSpec Counter(string name) =>
        new(name, JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true, Seed: 1, Increment: 1);
}