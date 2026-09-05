using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class BinaryLiteralTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "binlit-");
        return path;
    }

    // A raw 0x… binary literal (as Access emits for an OLE / Long Binary column, e.g. Categories.Picture)
    // parses and round-trips: a short value stays inline, a multi-KB one goes to LVAL pages.
    [Fact]
    public void Hex_binary_literal_round_trips()
    {
        string path = Fresh();
        try
        {
            byte[] small = [0x01, 0x02, 0x03, 0x04, 0x05];
            byte[] big = new byte[5000];
            new Random(42).NextBytes(big);

            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE Pics (Id LONG, Pic OLEOBJECT)");
            e.ExecuteNonQuery($"INSERT INTO Pics (Id, Pic) VALUES (1, 0x{Convert.ToHexString(small)})");
            e.ExecuteNonQuery($"INSERT INTO Pics (Id, Pic) VALUES (2, 0x{Convert.ToHexString(big)})");

            Assert.Equal(small, (byte[])e.ExecuteQuery("SELECT Pic FROM Pics WHERE Id = 1").Rows.First()[0]!);
            Assert.Equal(big, (byte[])e.ExecuteQuery("SELECT Pic FROM Pics WHERE Id = 2").Rows.First()[0]!);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Odd_length_hex_literal_is_rejected()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE Pics2 (Id LONG, Pic OLEOBJECT)");
            Assert.Throws<LibRed.Sql.Parsing.SqlParseException>(() =>
                e.ExecuteNonQuery("INSERT INTO Pics2 (Id, Pic) VALUES (1, 0x010)"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
