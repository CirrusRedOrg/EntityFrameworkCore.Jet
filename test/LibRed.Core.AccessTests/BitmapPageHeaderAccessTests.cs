using System.Data.OleDb;
using LibRed.Formats;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Bytes 2..3 of a usage-map bitmap page (type 0x0105) are not read: ACE reads and writes through a bitmap page
// whatever they hold, and keeps them. LibRed refused such a page as not a bitmap at all. The fixture is ACE's own:
// a table of one-row pages ending in a part-filled tail page, whose free map ACE converts to a reference map when
// a row far below that tail is deleted.
[Collection(AceCollection.Name)]
public class BitmapPageHeaderAccessTests : TempDatabaseTest
{
    private const int Rows = 1500;

    [Fact]
    public void Bytes_two_and_three_of_a_bitmap_page_are_kept_and_not_read()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "bitmap-hdr-");
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            Execute(connection, "CREATE TABLE R (Id LONG, C0 TEXT(255), C1 TEXT(255), C2 TEXT(255), C3 TEXT(255), C4 TEXT(255), C5 TEXT(255), C6 TEXT(255))");
            string full = new('a', 255);
            for (int id = 1; id <= Rows; id++)
                Execute(connection, $"INSERT INTO R VALUES ({id}, '{full}', '{full}', '{full}', '{full}', '{full}', '{full}', '{full}')");
            Execute(connection, "INSERT INTO R (Id, C0) VALUES (0, 'tail')");
            Execute(connection, "DELETE FROM R WHERE Id = 5");
        }

        int bitmap = FreeMapBitmapPage(path);
        byte[] page = TestDatabases.ReadPage(path, bitmap);
        Assert.Equal(new byte[] { 0x05, 0x01, 0x00, 0x00 }, page[..4]);
        page[2] = 0xA5;
        page[3] = 0x5A;
        TestDatabases.WritePage(path, bitmap, page);

        // ACE reads the map and writes through the page, and the bytes stay.
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            Execute(connection, $"INSERT INTO R (Id, C0) VALUES ({Rows + 1}, 'x')");
            Execute(connection, "DELETE FROM R WHERE Id = 7");
        }
        Assert.Equal(new byte[] { 0x05, 0x01, 0xA5, 0x5A }, TestDatabases.ReadPage(path, bitmap)[..4]);

        // So does LibRed.
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            Table table = database.OpenTable("R");
            var values = new object?[8];
            values[0] = Rows + 2;
            table.Insert(values);
            Assert.Equal(Rows + 1, table.Rows().Count());
        }
        Assert.Equal(new byte[] { 0x05, 0x01, 0xA5, 0x5A }, TestDatabases.ReadPage(path, bitmap)[..4]);
    }

    /// <summary>The first bitmap page of table R's free-pages map, which must be in reference form.</summary>
    private static int FreeMapBitmapPage(string path)
    {
        using var database = JetDatabase.Open(path);
        Table table = database.OpenTable("R");
        JetFormatBase format = database.Format;
        (int row, int page) = table.Channel.ReadPage(table.Definition.DefinitionPage).ReadRecordPointer(format.TdefFreePagesOffset);
        ReadOnlySpan<byte> record = new UsageMap(table.Channel, table.Definition).ReadRecordAt(row, page);
        Assert.Equal(UsageMapType.Reference, UsageMap.RecordType(record));
        int bitmap = UsageMap.ReferencePointer(record, 0, format);
        Assert.NotEqual(0, bitmap);
        return bitmap;
    }

    private static void Execute(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
