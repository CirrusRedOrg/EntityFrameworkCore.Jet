using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// The other end of RecordSizeAccessTests. ACE pads an all-fixed row's fixed region out to two bytes, so the
// shortest record it will write is five: the 2-byte column count, that 2-byte region, and a 1-byte null
// bitmap. It is a floor and not an alignment - a three-BYTE table keeps its odd 3-byte region - and a row
// with a variable trailer is exempt, keeping a region of 0 or 1.
//
// LibRed used to size the region from the table definition alone, one or two bytes short. That is not
// cosmetic: an all-Boolean table of eight columns or fewer then encodes to a 3-byte record, and ACE reads
// every Boolean in it as False. The cliff is at four bytes rather than five - sixteen Booleans, whose bitmap
// is two bytes wide, read back correctly - but ACE's own writer never emits a record under five, so the short
// form is simply a shape its reader has never met, and it misreads it silently rather than refusing it.
public class MinimumRecordSizeAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    /// <summary>The record bytes of the single row in <paramref name="table"/>.</summary>
    private static byte[] OnlyRecord(JetDatabase database, string table)
    {
        Table t = database.OpenTable(table);
        int page = t.UsageMap.DataPages().Single();
        return database.ReadDataPage(page).GetRow(0).ToArray();
    }

    // Natural length is 2 (column count) + fixed bytes + ceil(columns / 8) (null bitmap). Booleans occupy no
    // fixed bytes at all, which is how a shape gets a 0-byte region; nine of them is the one case that tells
    // "pad the region to 2" apart from "pad the record to 5", because ACE writes it as six bytes.
    [Theory]
    [InlineData("YESNO", "True", 5)]                                // region 0 -> 2
    [InlineData("BYTE", "1", 5)]                                    // region 1 -> 2
    [InlineData("BYTE, B YESNO", "1, True", 5)]                     // region 1 -> 2
    [InlineData("BYTE, B BYTE", "1, 2", 5)]                         // region 2, untouched
    [InlineData("BYTE, B BYTE, C BYTE", "1, 2, 3", 6)]              // region 3, odd and untouched
    [InlineData("SHORT", "1", 5)]                                   // region 2, untouched
    [InlineData("YESNO, B YESNO, C YESNO, D YESNO, E YESNO, F YESNO, G YESNO, H YESNO, I YESNO",
                "True, True, True, True, True, True, True, True, True", 6)]
    [InlineData("TEXT(10)", "'ab'", 13)]                            // variable trailer: region 0, untouched
    [InlineData("BYTE, B TEXT(10)", "1, 'ab'", 14)]                 // variable trailer: region 1, untouched
    public void Ace_pads_an_all_fixed_record_to_five_bytes(string declaration, string values, int expected)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "minrec-ace-");
        string names = string.Join(", ", Enumerable.Range(0, values.Split(',').Length).Select(i => (char)('A' + i)));

        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            using (OleDbCommand ddl = connection.CreateCommand())
            {
                ddl.CommandText = $"CREATE TABLE Narrow (A {declaration})";
                ddl.ExecuteNonQuery();
            }
            using OleDbCommand insert = connection.CreateCommand();
            insert.CommandText = $"INSERT INTO Narrow ({names}) VALUES ({values})";
            insert.ExecuteNonQuery();
        }

        using var database = JetDatabase.Open(path, readOnly: true);
        byte[] record = OnlyRecord(database, "Narrow");
        output.WriteLine($"{declaration}: {record.Length} bytes, {Convert.ToHexString(record)}");
        Assert.Equal(expected, record.Length);
    }

    // The pad is what ACE writes, so LibRed must write it too - byte for byte, not merely to the same length.
    // Both tables are built and filled by each engine in turn in the same file, which also pins that the
    // padding is a property of the record and not of the definition: the TDEF keeps the true fixed-row length
    // (ACE stores 1 for a BYTE table while writing 5-byte rows into it), so nothing in the definition would
    // carry this.
    [Theory]
    [InlineData(JetDataType.Boolean, "YESNO", true)]
    [InlineData(JetDataType.Byte, "BYTE", (byte)7)]
    public void Libred_writes_the_record_bytes_ace_writes(JetDataType type, string declaration, object value)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "minrec-parity-");

        using (OleDbConnection connection = AceTestDatabase.Open(path))
        using (OleDbCommand ddl = connection.CreateCommand())
        {
            ddl.CommandText = $"CREATE TABLE ByAce (V {declaration})";
            ddl.ExecuteNonQuery();
            ddl.CommandText = $"INSERT INTO ByAce (V) VALUES ({(value is bool ? "True" : value)})";
            ddl.ExecuteNonQuery();
        }

        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            database.CreateTable("ByLibRed", [new ColumnSpec("V", type, 1, IsFixedLength: true)]);
            database.OpenTable("ByLibRed").Insert([value]);
        }

        using var read = JetDatabase.Open(path, readOnly: true);
        byte[] ace = OnlyRecord(read, "ByAce"), ours = OnlyRecord(read, "ByLibRed");
        output.WriteLine($"{declaration}: ACE {Convert.ToHexString(ace)}, LibRed {Convert.ToHexString(ours)}");
        Assert.Equal(Convert.ToHexString(ace), Convert.ToHexString(ours));
    }

    // The regression itself, and the only shape that was actually corrupt rather than merely short: a table
    // of nothing but Booleans, whose row carries no fixed data at all. Without the pad ACE reports every one
    // of them False. Eight columns and sixteen bracket the point where the null bitmap grows to two bytes and
    // the record reaches four on its own.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(16)]
    public void Ace_reads_a_boolean_only_table_libred_wrote(int columns)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, $"minrec-bits{columns}-");
        bool[] pattern = Enumerable.Range(0, columns).Select(i => i % 3 != 1).ToArray();

        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            database.CreateTable("Bits", Enumerable.Range(0, columns)
                .Select(i => new ColumnSpec($"F{i}", JetDataType.Boolean, 1, IsFixedLength: true)).ToList());
            database.OpenTable("Bits").Insert(pattern.Select(b => (object?)b).ToArray());
        }

        using (var read = JetDatabase.Open(path, readOnly: true))
            output.WriteLine($"{columns} Booleans: {Convert.ToHexString(OnlyRecord(read, "Bits"))}");

        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand select = connection.CreateCommand();
        select.CommandText = "SELECT " + string.Join(", ", Enumerable.Range(0, columns).Select(i => $"F{i}"))
            + " FROM Bits";
        using OleDbDataReader rows = select.ExecuteReader();

        Assert.True(rows.Read());
        for (int i = 0; i < columns; i++)
            Assert.Equal(pattern[i], rows.GetBoolean(i));
        Assert.False(rows.Read());
    }
}
