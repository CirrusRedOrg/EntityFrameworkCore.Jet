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
// Each case builds its base database at the LOWEST format version its column type needs, rather than
// borrowing the Ace16Types fixture. That fixture is version 0x06 - ACE 17 / Access 2019 - and ACE opens a
// file only if it understands the whole format, so every case in this theory, Int32 and Double included,
// became unopenable on the ACE 2016 CI installs: "The database you are trying to open requires a newer
// version of Microsoft Access", which says nothing about the column under test.
//
// Everything but Int64 needs nothing beyond the 0x02 baseline and now runs everywhere. Int64 needs 0x05,
// and that is NOT within the CI engine's reach either, measured: with the six other cases passing at 0x02,
// the two Int64 ones still failed to open at 0x05. The "Access Database Engine 2016 Redistributable" is
// the 2016-era build, and Large Number arrived in a later servicing build - so "needs ACE 16" is about the
// Access version, not about anything named 2016. Hence the skip: an engine that cannot create a BIGINT
// cannot open a file holding one, and that says nothing about the descriptor behaviour under test.
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
        if (type == JetDataType.Int64)
            Assert.SkipUnless(
                AceTestDatabase.SupportsColumnType(TestDatabases.NorthwindAccdb, "BIGINT"),
                AceTestDatabase.UnsupportedColumnTypeReason("BIGINT"));

        object value = type switch
        {
            JetDataType.Int64 => 1234567890123456789L,
            JetDataType.Currency => 12345.6789m,
            JetDataType.DateTime => new DateTime(2024, 3, 4, 5, 6, 7),
            JetDataType.Int32 => 987654321,
            _ => 1234.5678d,
        };

        string path = TemporaryDatabase.CreatePath("fixedflag-");
        LibRed.Storage.DatabaseCreator.CreateEmpty(
            path, version: type == JetDataType.Int64 ? (byte)0x05 : (byte)0x02);
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
