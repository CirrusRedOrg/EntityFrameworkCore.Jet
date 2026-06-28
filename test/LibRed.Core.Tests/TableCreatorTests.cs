using LibRed;
using LibRed.Catalog;
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
