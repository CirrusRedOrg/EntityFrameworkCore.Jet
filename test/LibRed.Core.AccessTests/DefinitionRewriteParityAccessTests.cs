using System.Data.OleDb;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Rewriting a table definition through ACE and through LibRed, on copies of the same ACE-built file, must leave the
/// same file (docs/format/page-02a-tdef.md §3.2): the first page rewritten in place — alone, only the 8-byte reserve
/// past the new end zeroed — and continuation data moved to fresh pages, the last allocated first from the lowest
/// free pages, with the old continuation pages released untouched. The file has free pages mid-file, left by a
/// dropped table, so the allocation order shows.
/// </summary>
[Collection(AceCollection.Name)]
public class DefinitionRewriteParityAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    [Theory]
    [InlineData("a single page shrinking", "S", "DROP COLUMN c001")]
    [InlineData("a single page growing onto a second", "N", "ADD COLUMN z1")]
    [InlineData("two pages falling back to one", "M", "DROP COLUMN c001")]
    [InlineData("two pages growing", "W", "ADD COLUMN z1")]
    [InlineData("three pages shrinking", "V", "DROP COLUMN c001")]
    [InlineData("three pages growing by an index", "V", "CREATE INDEX")]
    public void Libred_rewrites_a_definition_byte_for_byte_with_ace(string label, string table, string rewrite)
    {
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "tdefrw-start-");
        using (OleDbConnection connection = AceTestDatabase.Open(start))
        {
            Exec(connection, $"CREATE TABLE S (Id LONG{Columns(10, "LONG")}, X TEXT(10))");
            Exec(connection, $"CREATE TABLE N (Id LONG{Columns(114, "LONG")})");
            Exec(connection, "CREATE TABLE Filler (Id LONG, T TEXT(255))");
            for (int i = 0; i < 60; i++) Exec(connection, $"INSERT INTO Filler (Id, T) VALUES ({i}, '{new string('f', 250)}')");
            Exec(connection, $"CREATE TABLE M (Id LONG{Columns(115, "LONG")})");
            Exec(connection, $"CREATE TABLE W (Id LONG{Columns(199, "CURRENCY")})");
            Exec(connection, $"CREATE TABLE V (Id LONG{Columns(254, "CURRENCY")})");
        }
        using (OleDbConnection connection = AceTestDatabase.Open(start))
            Exec(connection, "DROP TABLE Filler");

        string ace = TemporaryDatabase.CopyPath(start, "tdefrw-ace-");
        using (OleDbConnection connection = AceTestDatabase.Open(ace))
            Exec(connection, rewrite switch
            {
                "CREATE INDEX" => $"CREATE INDEX ix ON {table} (c001)",
                "ADD COLUMN z1" => $"ALTER TABLE {table} ADD COLUMN z1 LONG",
                _ => $"ALTER TABLE {table} {rewrite}",
            });

        string libred = TemporaryDatabase.CopyPath(start, "tdefrw-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
            switch (rewrite)
            {
                case "CREATE INDEX": db.CreateIndex(table, "ix", [("c001", false)]); break;
                case "ADD COLUMN z1": Assert.True(db.AddColumn(table, new ColumnSpec("z1", JetDataType.Int32, 4, IsFixedLength: true))); break;
                default: Assert.True(db.DropColumn(table, "c001")); break;
            }

        output.WriteLine(label);
        string difference = DropTableParityAccessTests.Difference(ace, libred);
        output.WriteLine(difference);
        Assert.Equal("", difference);
    }

    // A relationship adds an incoming block to the parent's definition. Into a single page after a drop, the 8
    // reserve bytes past the new end land on the old tail and are zeroed, the rest left; into a multi-page parent,
    // the continuation data moves to fresh pages. The relationship's own type-8 MSysObjects object and its two
    // MSysACEs rows are written as ACE writes them.
    [Theory]
    [InlineData("a shrunk single-page parent", false)]
    [InlineData("a multi-page parent", true)]
    public void Libred_adds_a_relationship_into_the_parents_definition_byte_for_byte_with_ace(string label, bool wide)
    {
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "tdefrel-start-");
        using (OleDbConnection connection = AceTestDatabase.Open(start))
        {
            Exec(connection, wide
                ? $"CREATE TABLE P (Id LONG CONSTRAINT pkP PRIMARY KEY{Columns(199, "CURRENCY")})"
                : "CREATE TABLE P (Id LONG CONSTRAINT pkP PRIMARY KEY, abcdefghijklmnopqrst LONG, V LONG)");
            Exec(connection, "CREATE TABLE C (Id LONG, PId LONG)");
        }
        if (!wide)
            using (OleDbConnection connection = AceTestDatabase.Open(start))
                Exec(connection, "ALTER TABLE P DROP COLUMN abcdefghijklmnopqrst");

        string ace = TemporaryDatabase.CopyPath(start, "tdefrel-ace-");
        using (OleDbConnection connection = AceTestDatabase.Open(ace))
            Exec(connection, "ALTER TABLE C ADD CONSTRAINT fkCP FOREIGN KEY (PId) REFERENCES P (Id)");

        string libred = TemporaryDatabase.CopyPath(start, "tdefrel-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
            db.AddForeignKey("C", new RelationshipSpec("fkCP", "P", [("PId", "Id")], IsEnforced: true, CascadeUpdate: false, CascadeDelete: false));

        output.WriteLine(label);
        string difference = DropTableParityAccessTests.Difference(ace, libred);
        output.WriteLine(difference);
        Assert.Equal("", difference);
    }

    private static string Columns(int count, string type) =>
        string.Concat(Enumerable.Range(1, count).Select(i => $", c{i:D3} {type}"));

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
