using System.Data.OleDb;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// WITH COMPRESSION end to end through the SQL front end: LibRed declares the column, writes the compressed
// form, and ACE reads the values back unchanged. The byte-level agreement with ACE's own writer is covered
// by CompressedTextAccessTests; this is the statement path and the round trip.
[Collection(AceCollection.Name)]
public class CompressedColumnAccessTests : TempDatabaseTest
{
    [Fact]
    public void Ace_reads_back_values_libred_wrote_to_compressed_columns()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "with-comp-");

        string longAscii = new('a', 1908);   // the last length that stays on one page, so compressed
        string chained = new('b', 1909);     // one more, so chained and never compressed
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            var engine = new QueryEngine(database);
            engine.ExecuteNonQuery(
                // Not named "Comp": that is the COMP keyword, so it needs quoting as an identifier.
                "CREATE TABLE Squeezed (Id LONG PRIMARY KEY, T TEXT(255) WITH COMPRESSION, M LONGCHAR WITH COMP)");
            engine.ExecuteNonQuery("INSERT INTO Squeezed (Id, T, M) VALUES (1, @t, @m)",
                new Dictionary<string, object?> { ["t"] = "hello world", ["m"] = longAscii });
            engine.ExecuteNonQuery("INSERT INTO Squeezed (Id, T, M) VALUES (2, @t, @m)",
                new Dictionary<string, object?> { ["t"] = "café", ["m"] = chained });
            // Not Latin1, so stored UTF-16 even though the column allows compression.
            engine.ExecuteNonQuery("INSERT INTO Squeezed (Id, T, M) VALUES (3, @t, @m)",
                new Dictionary<string, object?> { ["t"] = "一二三", ["m"] = "一" });
        }

        using var connection = AceTestDatabase.Open(path);
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT Id, T, M FROM Squeezed ORDER BY Id";
        using OleDbDataReader rows = read.ExecuteReader();

        Assert.True(rows.Read());
        Assert.Equal("hello world", rows.GetString(1));
        Assert.Equal(longAscii, rows.GetString(2));
        Assert.True(rows.Read());
        Assert.Equal("café", rows.GetString(1));
        Assert.Equal(chained, rows.GetString(2));
        Assert.True(rows.Read());
        Assert.Equal("一二三", rows.GetString(1));
        Assert.Equal("一", rows.GetString(2));
        Assert.False(rows.Read());
    }

    [Fact]
    public void Compression_is_refused_on_a_type_that_cannot_carry_it()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "with-comp-bad-");
        using var database = JetDatabase.Open(path, readOnly: false);
        var engine = new QueryEngine(database);

        var thrown = Assert.Throws<NotSupportedException>(
            () => engine.ExecuteNonQuery("CREATE TABLE Bad (Id LONG WITH COMPRESSION)"));
        Assert.Contains("text and memo", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }
}
