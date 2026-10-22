using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Engine.Tests;

// ACE's side of IndexKeyRowAgreementTests: a row LibRed writes has to be one ACE can find through its index and
// then update and delete. Before the key was built from the stored value, a True LibRed wrote to an indexed SHORT
// column was invisible to ACE's `S = -1`, matched its `S = 1`, and ACE refused UPDATE and DELETE of the row with
// "The search key was not found in any record."
[Collection(AceCollection.Name)]
public class IndexKeyRowAgreementAccessTests : TempDatabaseTest
{
    private static string AceTable(string ddl)
    {
        string path = TemporaryDatabase.CreatePath("key-row-ace-");
        JetDatabase.Create(path);
        using OleDbConnection connection = AceTestDatabase.Open(path);
        Execute(connection, ddl);
        Execute(connection, "CREATE INDEX IX ON T (S)");
        // Enough rows that ACE answers `S = …` through the index rather than a scan.
        for (int i = 1; i <= 300; i++) Execute(connection, $"INSERT INTO T (S) VALUES ({1000 + i})");
        return path;
    }

    private static int Execute(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    private static int Count(OleDbConnection connection, string where)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM T WHERE {where}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    [Fact]
    public void Ace_finds_updates_and_deletes_a_True_LibRed_wrote_to_an_indexed_SHORT()
    {
        string path = AceTable("CREATE TABLE T (ID COUNTER PRIMARY KEY, S SHORT)");
        using (JetDatabase db = JetDatabase.Open(path, readOnly: false))
            new QueryEngine(db).ExecuteNonQuery("INSERT INTO T (S) VALUES (True)");

        using OleDbConnection connection = AceTestDatabase.Open(path);
        Assert.Equal(1, Count(connection, "S = -1"));
        Assert.Equal(0, Count(connection, "S = 1"));
        Assert.Equal(1, Execute(connection, "UPDATE T SET S = 5 WHERE ID = 301"));
        Assert.Equal(1, Execute(connection, "DELETE FROM T WHERE ID = 301"));
    }

    [Fact]
    public void Ace_and_LibRed_both_refuse_the_padded_form_of_a_short_value_in_a_unique_BINARY()
    {
        string path = TemporaryDatabase.CreatePath("key-row-binary-");
        JetDatabase.Create(path);
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            Execute(connection, "CREATE TABLE T (ID COUNTER PRIMARY KEY, S BINARY(4))");
            Execute(connection, "CREATE UNIQUE INDEX UX ON T (S)");
            Execute(connection, "INSERT INTO T (S) VALUES (0x0102)");
            Assert.ThrowsAny<OleDbException>(() => Execute(connection, "INSERT INTO T (S) VALUES (0x01020000)"));
        }

        using JetDatabase db = JetDatabase.Open(path, readOnly: false);
        Assert.Throws<ConstraintViolationException>(
            () => new QueryEngine(db).ExecuteNonQuery("INSERT INTO T (S) VALUES (0x01020000)"));
    }
}
