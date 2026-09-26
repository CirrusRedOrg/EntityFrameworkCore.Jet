using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Where a table's property blob (<c>MSysObjects.LvProp</c>) is stored: inline in the row up to 64 bytes and on an
/// LVAL page above that, the rule every long value follows — through ACE and through LibRed alike. Each property adds
/// its owner's name to the blob, so the length of a <c>NOT NULL</c> column's name walks the blob across the line.
/// </summary>
[Collection(AceCollection.Name)]
public class LvPropStorageAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    [Theory]
    [InlineData("CREATE TABLE W (Id LONG, Cxxxxx LONG NOT NULL)")]             // 61 bytes
    [InlineData("CREATE TABLE W (Id LONG, Cxxxxxx LONG NOT NULL)")]            // 63 bytes
    [InlineData("CREATE TABLE W (Id LONG, Cxxxxxxx LONG NOT NULL)")]           // 65 bytes
    [InlineData("CREATE TABLE W (Id LONG, Cxxxxxxxx LONG NOT NULL)")]          // 67 bytes
    [InlineData("CREATE TABLE W (Id LONG, A LONG DEFAULT 7)")]                  // 60 bytes
    [InlineData("CREATE TABLE W (Id LONG, A LONG DEFAULT 7, B LONG DEFAULT 8)")] // 84 bytes
    public void A_property_blob_is_stored_where_ace_stores_it(string sql)
    {
        string ace = Stored(sql, AceRun), libred = Stored(sql, LibRedRun);
        output.WriteLine($"ACE {ace}, LibRed {libred}");
        Assert.Equal(ace, libred);
    }

    // What an inline blob is for: ACE reads it. A NOT NULL written inline by LibRed refuses a null in ACE, and a
    // DEFAULT written inline fills an omitted column.
    private static readonly string Northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    [Fact]
    public void Ace_honours_a_property_blob_libred_wrote_inline()
    {
        string path = TemporaryDatabase.CopyPath(Northwind, "lvpropinline-");
        try
        {
            LibRedRun(path, "CREATE TABLE W (Id LONG, A LONG NOT NULL)");
            LibRedRun(path, "CREATE TABLE V (Id LONG, A LONG DEFAULT 7)");
            Assert.StartsWith("inline", Stored(path, "W"), StringComparison.Ordinal);
            Assert.StartsWith("inline", Stored(path, "V"), StringComparison.Ordinal);

            using OleDbConnection connection = AceTestDatabase.Open(path);
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO W (Id) VALUES (1)";
            Assert.Throws<OleDbException>(() => command.ExecuteNonQuery());
            command.CommandText = "INSERT INTO V (Id) VALUES (1)";
            command.ExecuteNonQuery();
            command.CommandText = "SELECT A FROM V";
            Assert.Equal(7, command.ExecuteScalar());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string Stored(string sql, Action<string, string> create)
    {
        string path = TemporaryDatabase.CopyPath(Northwind, "lvpropstore-");
        try
        {
            create(path, sql);
            return Stored(path, "W");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>How table <paramref name="table"/>'s blob is stored, and its length.</summary>
    private static string Stored(string path, string table)
    {
        using var database = JetDatabase.Open(path, readOnly: true);
        Table objects = database.OpenTable("MSysObjects");
        TableDef definition = objects.Definition;
        int name = definition.RequireColumn("Name").Index, lvProp = definition.RequireColumn("LvProp").Index;
        foreach ((RowId id, object?[] values) in objects.Rows().WithIds())
        {
            if (values[name] as string != table) continue;
            var page = new DataPage();
            page.Read(objects.Channel.ReadPageShared(id.Page), objects.Channel.Format);
            byte[] descriptor = RowDecoder.LongValueDescriptors(
                definition.Columns, objects.Channel.Format, page.GetRow(id.Row))[lvProp];
            string form = descriptor[3] switch { 0x80 => "inline", 0x40 => "on a page", _ => $"0x{descriptor[3]:X2}" };
            return $"{form}, {((byte[])values[lvProp]!).Length} bytes";
        }
        throw new InvalidOperationException($"No MSysObjects row for {table}.");
    }

    private static void AceRun(string path, string sql)
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void LibRedRun(string path, string sql)
    {
        using var database = JetDatabase.Open(path, readOnly: false);
        new QueryEngine(database).ExecuteNonQuery(sql);
    }
}
