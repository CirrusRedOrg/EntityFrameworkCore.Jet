using System.Data.OleDb;
using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// ACE's own <c>TOP n</c> keeps every row that ties the last on the ORDER BY keys, so LibRed's <c>TOP n WITH TIES</c>
/// must choose the same rows. ACE orders tied rows unstably, so the rows are compared as a set, not as a sequence.
/// </summary>
[Collection(AceCollection.Name)]
public class TopWithTiesAccessTests : TempDatabaseTest
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, K LONG, K2 TEXT(10))",
        "INSERT INTO T VALUES (1, 1, 'a')",
        "INSERT INTO T VALUES (2, 2, 'b')",
        "INSERT INTO T VALUES (3, 2, 'a')",
        "INSERT INTO T VALUES (4, 2, 'b')",
        "INSERT INTO T VALUES (5, 3, 'a')",
        "INSERT INTO T VALUES (6, 3, 'a')",
        "INSERT INTO T VALUES (7, 10, 'c')",
        "INSERT INTO T VALUES (8, NULL, NULL)",
        "INSERT INTO T VALUES (9, NULL, 'z')",
    ];

    [Theory]
    [InlineData("TOP 1", "ORDER BY K")]
    [InlineData("TOP 3", "ORDER BY K")]
    [InlineData("TOP 4", "ORDER BY K")]
    [InlineData("TOP 6", "ORDER BY K")]
    [InlineData("TOP 3", "ORDER BY K, K2")]
    [InlineData("TOP 2", "ORDER BY K DESC")]
    [InlineData("TOP 5", "ORDER BY K2")]
    [InlineData("TOP 2", "ORDER BY K MOD 2")]
    [InlineData("TOP 30 PERCENT", "ORDER BY K")]
    [InlineData("TOP 50 PERCENT", "ORDER BY K")]
    public void Libred_with_ties_takes_the_rows_ace_top_takes(string top, string orderBy)
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ties-");
        try
        {
            List<int> ace;
            using (OleDbConnection connection = AceTestDatabase.Open(path))
            {
                foreach (string statement in Setup)
                {
                    using OleDbCommand setup = connection.CreateCommand();
                    setup.CommandText = statement;
                    setup.ExecuteNonQuery();
                }
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT {top} Id FROM T {orderBy}";
                using OleDbDataReader reader = command.ExecuteReader();
                ace = [];
                while (reader.Read()) ace.Add(reader.GetInt32(0));
            }
            OleDbConnection.ReleaseObjectPool();

            List<int> libred;
            using (var database = JetDatabase.Open(path, readOnly: true))
                libred = [.. new QueryEngine(database).ExecuteQuery($"SELECT {top} WITH TIES Id FROM T {orderBy}")
                    .Rows.Select(row => Convert.ToInt32(row[0], CultureInfo.InvariantCulture))];

            Assert.Equal(ace.Order(), libred.Order());
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
