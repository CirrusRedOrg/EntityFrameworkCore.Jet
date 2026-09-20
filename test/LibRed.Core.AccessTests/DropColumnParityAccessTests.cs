using System.Data.OleDb;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// DROP COLUMN of a memo or OLE column through ACE and through LibRed, on copies of the same ACE-built file, must leave
/// the same file: the column's long-value map entry gone from the definition, its owned and free map records retired
/// from their holder as DROP TABLE retires them, and the pages it owned back in the global free map
/// (docs/format/long-values.md).
/// </summary>
[Collection(AceCollection.Name)]
public class DropColumnParityAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    // Each column holds an inline value, a single-page value and a chained one. With full pages, five more values
    // each fill half a page, so the column also owns full single-value pages that are no longer in its free map.
    [Theory]
    [InlineData("M1", false)]
    [InlineData("O1", false)]
    [InlineData("M1", true)]
    [InlineData("O1", true)]
    public void Libred_drops_a_long_value_column_byte_for_byte_with_ace(string column, bool fullPages)
    {
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "dropcolpar-start-");
        using (OleDbConnection connection = AceTestDatabase.Open(start))
        {
            Exec(connection, "CREATE TABLE T (Id LONG, M1 MEMO, X TEXT(10), M2 MEMO, O1 LONGBINARY)");
            Insert(connection, 1, "short", "two", Bytes(10));
            Insert(connection, 2, new string('a', 1000), new string('b', 900), Bytes(1500));
            Insert(connection, 3, new string('c', 10000), "mid", Bytes(9000));
            if (fullPages)
                for (int id = 4; id <= 8; id++)
                    Insert(connection, id, new string('f', 1010), "m", Bytes(2020));
        }

        string ace = TemporaryDatabase.CopyPath(start, "dropcolpar-ace-");
        using (OleDbConnection connection = AceTestDatabase.Open(ace))
            Exec(connection, $"ALTER TABLE T DROP COLUMN {column}");

        string libred = TemporaryDatabase.CopyPath(start, "dropcolpar-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
            Assert.True(db.DropColumn("T", column));

        string difference = DropTableParityAccessTests.Difference(ace, libred);
        output.WriteLine(difference);
        Assert.Equal("", difference);

        // ACE reads the surviving long values from the file LibRed wrote.
        using OleDbConnection check = AceTestDatabase.Open(libred);
        using OleDbCommand read = check.CreateCommand();
        read.CommandText = "SELECT M2 FROM T WHERE Id = 2";
        Assert.Equal(new string('b', 900), read.ExecuteScalar());
    }

    private static byte[] Bytes(int n) => Enumerable.Range(0, n).Select(i => (byte)(i * 7 + 1)).ToArray();

    private static void Insert(OleDbConnection connection, int id, string m1, string m2, byte[] o1)
    {
        using OleDbCommand insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO T (Id, M1, X, M2, O1) VALUES ({id}, ?, 'x', ?, ?)";
        insert.Parameters.Add("m1", OleDbType.LongVarWChar).Value = m1;
        insert.Parameters.Add("m2", OleDbType.LongVarWChar).Value = m2;
        insert.Parameters.Add("o1", OleDbType.LongVarBinary).Value = o1;
        insert.ExecuteNonQuery();
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
