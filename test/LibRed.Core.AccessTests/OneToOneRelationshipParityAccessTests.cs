using System.Data.OleDb;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Adding a one-to-one relationship through ACE and through LibRed, on copies of the same ACE-built file, must leave
/// the same file. ACE's SQL cannot declare one, so its side is DAO's <c>CreateRelation</c> with
/// <c>dbRelationUnique</c>: <c>grbit</c> <c>0x01</c> on every <c>MSysRelationships</c> row, and the child's backing
/// index unique — sharing the block of a unique index over the same columns, but not the primary key's.
/// </summary>
[Collection(AceCollection.Name)]
public class OneToOneRelationshipParityAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    private const int OneToOne = 1, DontEnforce = 2;

    [Theory]
    [InlineData("ChildPlain", "ParentId", "Parent", "Id", true)]   // no index on the child column
    [InlineData("ChildUnique", "ParentId", "Parent", "Id", true)]  // a unique index over it already, which it shares
    [InlineData("ChildPk", "Id", "Parent", "Id", true)]            // the child's primary key, which it does not share
    [InlineData("ChildTwo", "A,B", "Parent2", "A,B", true)]        // two columns: 0x01 on both rows
    [InlineData("ChildNulls", "ParentId", "Parent", "Id", true)]   // two null keys do not collide
    [InlineData("ChildPlain", "ParentId", "Parent", "Id", false)]  // unenforced: the rows alone, grbit 0x03
    public void Libred_adds_a_one_to_one_byte_for_byte_with_ace(
        string child, string columns, string parent, string referenced, bool enforced)
    {
        string start = CreateStart();

        string ace = TemporaryDatabase.CopyPath(start, "one-ace-");
        Assert.Equal("related", DaoRelate(ace, child, columns, parent, referenced, enforced ? OneToOne : OneToOne | DontEnforce));

        string libred = TemporaryDatabase.CopyPath(start, "one-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
            db.AddForeignKey(child, Spec(child, columns, parent, referenced, enforced));

        string difference = DropTableParityAccessTests.Difference(ace, libred);
        output.WriteLine(difference);
        Assert.Equal("", difference);

        using var reopened = JetDatabase.Open(libred, readOnly: true);
        Assert.True(reopened.Catalog.Relationships.Single(r => r.Name == $"FK_{child}").IsOneToOne);
    }

    // Child rows sharing a key cannot take the unique index: ACE refuses the relationship, having written its
    // MSysObjects object first, so the rollback leaves MSysObjects' and MSysACEs' counting behind; so does LibRed.
    [Fact]
    public void Libred_refuses_a_one_to_one_over_duplicate_keys_as_ace_does()
    {
        string start = CreateStart();

        string ace = TemporaryDatabase.CopyPath(start, "one-ace-");
        Assert.Contains("duplicate values", DaoRelate(ace, "ChildDup", "ParentId", "Parent", "Id", OneToOne));

        string libred = TemporaryDatabase.CopyPath(start, "one-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
            Assert.Throws<InvalidOperationException>(() =>
                db.AddForeignKey("ChildDup", Spec("ChildDup", "ParentId", "Parent", "Id", enforced: true)));

        string difference = DropTableParityAccessTests.Difference(ace, libred);
        output.WriteLine(difference);
        Assert.Equal("", difference);

        using var reopened = JetDatabase.Open(libred, readOnly: true);
        Assert.DoesNotContain(reopened.Catalog.Relationships, r => r.Name == "FK_ChildDup");
    }

    private static RelationshipSpec Spec(string child, string columns, string parent, string referenced, bool enforced) => new(
        Name: $"FK_{child}",
        ReferencedTable: parent,
        Columns: RelationshipSpec.PairColumns(parent, columns.Split(','), referenced.Split(',')),
        IsEnforced: enforced,
        CascadeUpdate: false,
        CascadeDelete: false,
        IsOneToOne: true);

    private static string CreateStart()
    {
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "one-start-");
        using (OleDbConnection connection = AceTestDatabase.Open(start))
            foreach (string sql in (string[])
                     [
                         "CREATE TABLE Parent (Id LONG, CONSTRAINT PK_Parent PRIMARY KEY (Id))",
                         "CREATE TABLE Parent2 (A LONG, B LONG, CONSTRAINT PK_Parent2 PRIMARY KEY (A, B))",
                         "CREATE TABLE ChildPlain (Id LONG, ParentId LONG, CONSTRAINT PK_ChildPlain PRIMARY KEY (Id))",
                         "CREATE TABLE ChildUnique (Id LONG, ParentId LONG, CONSTRAINT PK_ChildUnique PRIMARY KEY (Id))",
                         "CREATE UNIQUE INDEX IX_ChildUnique_ParentId ON ChildUnique (ParentId)",
                         "CREATE TABLE ChildPk (Id LONG, CONSTRAINT PK_ChildPk PRIMARY KEY (Id))",
                         "CREATE TABLE ChildTwo (Id LONG, A LONG, B LONG, CONSTRAINT PK_ChildTwo PRIMARY KEY (Id))",
                         "CREATE TABLE ChildDup (Id LONG, ParentId LONG, CONSTRAINT PK_ChildDup PRIMARY KEY (Id))",
                         "CREATE TABLE ChildNulls (Id LONG, ParentId LONG, CONSTRAINT PK_ChildNulls PRIMARY KEY (Id))",
                         "INSERT INTO Parent VALUES (1)",
                         "INSERT INTO Parent VALUES (2)",
                         "INSERT INTO Parent2 VALUES (1, 1)",
                         "INSERT INTO ChildPlain VALUES (1, 1)",
                         "INSERT INTO ChildUnique VALUES (1, 1)",
                         "INSERT INTO ChildPk VALUES (1)",
                         "INSERT INTO ChildTwo VALUES (1, 1, 1)",
                         "INSERT INTO ChildDup VALUES (1, 1)",
                         "INSERT INTO ChildDup VALUES (2, 1)",
                         "INSERT INTO ChildNulls VALUES (1, NULL)",
                         "INSERT INTO ChildNulls VALUES (2, NULL)",
                     ])
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        OleDbConnection.ReleaseObjectPool();
        return start;
    }

    /// <summary>"related", or the message DAO refused the relationship with. DAO: CreateRelation(name, primary table,
    /// foreign table, attributes); Field.Name = primary field, Field.ForeignName = foreign field.</summary>
    private static string DaoRelate(string path, string child, string columns, string parent, string referenced, int attributes)
    {
        dynamic engine = AceTestDatabase.CreateDaoEngine()
            ?? throw new InvalidOperationException("DAO is not registered in this process.");
        dynamic database = engine.OpenDatabase(path);
        try
        {
            string[] childColumns = columns.Split(','), parentColumns = referenced.Split(',');
            dynamic relation = database.CreateRelation($"FK_{child}", parent, child, attributes);
            for (int i = 0; i < parentColumns.Length; i++)
            {
                dynamic field = relation.CreateField(parentColumns[i]);
                field.ForeignName = childColumns[i];
                relation.Fields.Append(field);
            }
            database.Relations.Append(relation);
            return "related";
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
}
