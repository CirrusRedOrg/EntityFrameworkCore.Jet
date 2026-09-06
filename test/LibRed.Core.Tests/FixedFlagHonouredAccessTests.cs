using System.Data.OleDb;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// ACE honours the descriptor's fixed-length flag; it does not look for a value where IT would have put
// that type.
//
// It has to, structurally: ACE's own MSysComplexType_GUID declares Value FIXED while every GUID column its
// DDL creates is VARIABLE, so one engine reads both layouts routinely. This measures it rather than
// arguing it, because data-types.md previously asserted the opposite for BIGINT - that declaring it fixed
// "would write the value somewhere ACE does not look for it" - which was inferred from what ACE writes and
// is not true.
//
// What follows for LibRed: where a type sits is a WRITE-side faithfulness question (match what ACE's DDL
// produces - see GuidColumnStorageAccessTests) and never a read-side hazard. A guard against re-deriving
// that claim next time the question comes up.
//
// Ace16Types.accdb is already ACE 16, so an Int64 column needs no version raise.
public class FixedFlagHonouredAccessTests : TempDatabaseTest
{
    public static TheoryData<JetDataType, int, bool> Shapes => new()
    {
        // Types ACE keeps in the variable region, declared fixed.
        { JetDataType.Int64, 8, true },
        // Types ACE keeps in the fixed region, declared variable.
        { JetDataType.Int32, 4, false },
        { JetDataType.Double, 8, false },
        { JetDataType.Currency, 8, false },
        { JetDataType.DateTime, 8, false },
        // And the same types the way ACE writes them, so a pass says something about the layout rather
        // than about the reader ignoring the column.
        { JetDataType.Int64, 8, false },
        { JetDataType.Currency, 8, true },
        { JetDataType.DateTime, 8, true },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Ace_reads_a_value_from_whichever_region_the_descriptor_names(
        JetDataType type, int width, bool isFixed)
    {
        object value = type switch
        {
            JetDataType.Int64 => 1234567890123456789L,
            JetDataType.Currency => 12345.6789m,
            JetDataType.DateTime => new DateTime(2024, 3, 4, 5, 6, 7),
            JetDataType.Int32 => 987654321,
            _ => 1234.5678d,
        };

        string path = TemporaryDatabase.CopyPath(TestDatabases.Data("Ace16Types.accdb"), "fixedflag-");
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            // Neighbours either side: had the value landed in the wrong region, these would shift.
            database.CreateTable("W",
                [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                 new ColumnSpec("Before", JetDataType.Text, 20, IsFixedLength: false),
                 new ColumnSpec("V", type, width, IsFixedLength: isFixed),
                 new ColumnSpec("After", JetDataType.Int32, 4, IsFixedLength: true)],
                primaryKey: ["Id"]);
            database.OpenTable("W").Insert([1, "before", value, 42]);
        }

        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand read = connection.CreateCommand();
        read.CommandText = "SELECT V, Before, After FROM W WHERE Id = 1";
        using OleDbDataReader reader = read.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal(value, Convert.ChangeType(reader.GetValue(0), value.GetType()));
        Assert.Equal("before", reader.GetString(1));
        Assert.Equal(42, reader.GetInt32(2));
    }
}
