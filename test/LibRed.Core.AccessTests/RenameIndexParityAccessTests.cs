using System.Data.OleDb;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Renaming an index through ACE and through LibRed, on copies of the same ACE-built file, must leave the same file.
/// ACE has no SQL for it, so its side is DAO's <c>Index.Name</c>, which rewrites only the table's definition: the
/// logical block moves to where its new name sorts, its bytes unchanged.
/// </summary>
[Collection(AceCollection.Name)]
public class RenameIndexParityAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    [Theory]
    [InlineData("Child", "IX_Label", "IX_Label_Renamed")]
    [InlineData("Child", "IX_Label", "AA_Label")]          // moves ahead of FK_Child_Parent in name order
    [InlineData("Child", "IX_Label", "ix_label")]          // case only
    [InlineData("Parent", "PK_Parent", "PK_Parent_Renamed")] // a key a relationship references
    [InlineData("Parent", "PK_Parent", "A")]               // shrinking
    [InlineData("Wide", "IX_Wide", "IX_Wide_Renamed_To_A_Much_Longer_Name")] // a multi-page definition
    public void Libred_renames_an_index_byte_for_byte_with_ace(string table, string index, string newName)
    {
        string start = CreateStart();

        string ace = TemporaryDatabase.CopyPath(start, "rnix-ace-");
        Assert.Equal("renamed", DaoRename(ace, table, index, newName));

        string libred = TemporaryDatabase.CopyPath(start, "rnix-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
            Assert.True(db.RenameIndex(table, index, newName));

        string difference = DropTableParityAccessTests.Difference(ace, libred);
        output.WriteLine(difference);
        Assert.Equal("", difference);
    }

    // ACE refuses the index a relationship created on its child table, and a name another index has, leaving the
    // file as it was; so does LibRed.
    [Theory]
    [InlineData("Child", "FK_Child_Parent", "FK_Child_Parent_Renamed")]
    [InlineData("Child", "IX_Label", "PK_Child")]
    public void Libred_refuses_a_rename_ace_refuses(string table, string index, string newName)
    {
        string start = CreateStart();

        string ace = TemporaryDatabase.CopyPath(start, "rnix-ace-");
        Assert.NotEqual("renamed", DaoRename(ace, table, index, newName));

        string libred = TemporaryDatabase.CopyPath(start, "rnix-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
            Assert.Throws<InvalidOperationException>(() => db.RenameIndex(table, index, newName));

        Assert.Equal("", DropTableParityAccessTests.Difference(start, ace));
        Assert.Equal("", DropTableParityAccessTests.Difference(start, libred));
    }

    private static string CreateStart()
    {
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "rnix-start-");
        using (OleDbConnection connection = AceTestDatabase.Open(start))
        {
            Exec(connection, "CREATE TABLE Parent (Id LONG, CONSTRAINT PK_Parent PRIMARY KEY (Id))");
            Exec(connection, "CREATE TABLE Child (Id LONG, ParentId LONG, Label TEXT(10), CONSTRAINT PK_Child PRIMARY KEY (Id), " +
                             "CONSTRAINT FK_Child_Parent FOREIGN KEY (ParentId) REFERENCES Parent (Id))");
            Exec(connection, "CREATE INDEX IX_Label ON Child (Label)");
            Exec(connection, "INSERT INTO Parent VALUES (1)");
            Exec(connection, "INSERT INTO Child VALUES (1, 1, 'a')");
            Exec(connection, $"CREATE TABLE Wide (Id LONG{string.Concat(Enumerable.Range(1, 254).Select(i => $", c{i:D3} CURRENCY"))})");
            Exec(connection, "CREATE INDEX IX_Wide ON Wide (c001)");
        }
        OleDbConnection.ReleaseObjectPool();
        return start;
    }

    /// <summary>"renamed", or the message DAO refused the rename with.</summary>
    private static string DaoRename(string path, string table, string index, string newName)
    {
        dynamic engine = AceTestDatabase.CreateDaoEngine()
            ?? throw new InvalidOperationException("DAO is not registered in this process.");
        dynamic database = engine.OpenDatabase(path);
        try
        {
            database.TableDefs[table].Indexes[index].Name = newName;
            return "renamed";
        }
        catch (System.Runtime.InteropServices.COMException e)
        {
            return e.Message;
        }
        finally
        {
            database.Close();
            AceTestDatabase.ReleaseAbandonedComObjects();
        }
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
