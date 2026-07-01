using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class TableCreatorTests
{
    private static string CopyToTemp()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-create-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
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
        finally { File.Delete(path); }
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
        finally { File.Delete(path); }
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
        finally { File.Delete(path); }
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
        finally { File.Delete(path); }
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
        finally { File.Delete(path); }
    }
}
