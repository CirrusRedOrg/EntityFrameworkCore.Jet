using System.Data.OleDb;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// A GUID column is a fixed 16 bytes of data that ACE nonetheless keeps in the row's VARIABLE region.
//
// Exactly the shape already established for BIGINT (docs/format/data-types.md); GUID was the sibling that
// kept the fixed mapping. It is not a fallback ACE reaches for on wide tables - every GUID column ACE
// declares is variable, at one column or at 252.
//
// ACE reads either layout back correctly, unlike BIGINT, so this was never wrong values; it was LibRed
// writing a table ACE would not have written. The cost was real all the same: 16 fixed bytes per column
// spend record budget ACE does not spend, so a 252-GUID table ACE creates without complaint used to exceed
// LibRed's declared-record limit (ColumnWidthLimitAccessTests). The mapping itself lives in the engine, so
// LibRed's own side of this is asserted in LibRed.Engine.AccessTests.
public class GuidColumnStorageAccessTests : TempDatabaseTest
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(252)]
    public void Ace_declares_every_guid_column_variable_length(int count)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "guid-storage-");
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            using OleDbCommand create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE W (Id LONG, " + string.Join(", ",
                Enumerable.Range(0, count).Select(i => $"G{i} GUID")) + ")";
            create.ExecuteNonQuery();
        }

        using var database = JetDatabase.Open(path, readOnly: true);
        TableDef table = database.Catalog.FindTable("W")!;
        Assert.All(table.Columns.Where(c => c.Name.StartsWith('G')), column =>
        {
            Assert.False(column.IsFixedLength);
            Assert.Equal(16, column.Length);
        });
        Assert.Equal(4, table.Columns.Where(c => c.IsFixedLength).Sum(c => c.Length));   // only Id
    }

    // Declaring the table is not the same as filling a row of it. ACE budgets only the fixed region when it
    // opens a file, so 252 variable GUID columns declare happily where 252 fixed ones cannot exist at all —
    // but the values still have to fit a record when written, and the variable offset table is charged for
    // every one of the 252 columns whether populated or not (506 bytes). Both engines refuse identically.
    [Theory]
    [InlineData(200, true)]
    [InlineData(250, false)]
    public void A_wide_guid_table_declares_but_its_rows_still_face_the_record_cap(int populated, bool fits)
    {
        var value = Guid.Parse("11111111-1111-1111-1111-111111111111");
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "wideguid-row-");
        using var database = JetDatabase.Open(path, readOnly: false);
        database.CreateTable("W", [.. Enumerable.Range(0, 252)
            .Select(i => new ColumnSpec($"G{i}", JetDataType.Guid, 16, IsFixedLength: false))]);

        var values = new object?[252];
        for (int i = 0; i < populated; i++) values[i] = value;

        if (!fits)
        {
            // ACE refuses the same row with "Record is too large." — verified alongside this.
            Assert.Throws<InvalidOperationException>(() => database.OpenTable("W").Insert(values));
            return;
        }

        database.OpenTable("W").Insert(values);
        Assert.Equal(value, database.OpenTable("W").Rows().Single()[0]);
    }

    // The exception, and the reason "GUID is variable" must not be applied everywhere: ACE's OWN system
    // tables declare it fixed, which is what DatabaseCreator reproduces when LibRed synthesises a database.
    // Checked against real ACE-created files, not against anything LibRed wrote.
    [Theory]
    [InlineData("Ace16Types.accdb")]
    [InlineData("Database4.accdb")]
    [InlineData("BuiltInDataTypes.accdb")]
    public void Aces_own_complex_type_system_table_declares_it_fixed(string fixtureName)
    {
        using var database = JetDatabase.Open(TestDatabases.Data(fixtureName), readOnly: true);
        ColumnDef value = database.Catalog.FindTable("MSysComplexType_GUID")!.FindColumn("Value")!;

        Assert.True(value.IsFixedLength);
        Assert.Equal(16, value.Length);
    }

    // Both layouts round-trip through ACE, so the fixed form was never a wrong-value bug — a value landing
    // in the wrong region would have shifted the neighbours either side of it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ace_reads_a_guid_value_back_from_either_layout(bool isFixed)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "guid-value-");
        var value = Guid.Parse("0f7b3c2a-1111-4222-8333-abcdef012345");
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            database.CreateTable("W",
                [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                 new ColumnSpec("Before", JetDataType.Text, 20, IsFixedLength: false),
                 new ColumnSpec("G", JetDataType.Guid, 16, IsFixedLength: isFixed),
                 new ColumnSpec("After", JetDataType.Int32, 4, IsFixedLength: true)],
                primaryKey: ["Id"]);
            database.OpenTable("W").Insert([1, "before", value, 42]);
        }

        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand read = connection.CreateCommand();
        read.CommandText = "SELECT G, Before, After FROM W WHERE Id = 1";
        using OleDbDataReader reader = read.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(value, reader.GetGuid(0));
        Assert.Equal("before", reader.GetString(1));
        Assert.Equal(42, reader.GetInt32(2));
    }
}
