using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using LibRed.Tests.Shared;
using Xunit;

namespace LibRed.Core.Tests;

// The TDEF-level linkage a relationship writes into both tables — the parent index it points at, and the
// index_num that pairs the child's outgoing block with the parent's incoming one. Neither is visible in
// MSysRelationships, so ACE reading the result is the only proof the numbering is right.
[Collection(AceCollection.Name)]
public class RelationshipLinkageAccessTests(ITestOutputHelper output)
{
    // LibRed accepted a relationship whose parent key was a plain non-unique index. ACE refuses it — "No
    // unique index found for the referenced field of the primary table" — so LibRed was writing a
    // relationship ACE would not have created, over an index the DROP guard did not consider protected.
    [Fact]
    public void A_foreign_key_needs_a_unique_parent_index()
    {
        string path = TemporaryDatabase.CreatePath("fk-parent-unique-");
        try
        {
            using var db = JetDatabase.Open(CreateWithAce(path, [
                "CREATE TABLE P (A LONG, Descr TEXT(20))",
                "CREATE INDEX IXP ON P (A)",                    // NOT unique
                "CREATE TABLE C (B LONG)",
            ]), readOnly: false);

            var error = Assert.Throws<InvalidOperationException>(() => db.AddForeignKey("C",
                new RelationshipSpec("FK", "P", [("B", "A")], IsEnforced: true, CascadeUpdate: false, CascadeDelete: false)));
            output.WriteLine(error.Message);
            Assert.Contains("No unique index found", error.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A parent's incoming-relationship index_num came from the logical block COUNT, while the child side used
    // max + 1. Dropping a relationship removes a block without renumbering index_num, so after a drop the
    // count sits below the max and the next incoming block collided with a live one.
    [Fact]
    public void An_incoming_relationship_number_does_not_collide_after_a_drop()
    {
        string path = TemporaryDatabase.CreatePath("fk-index-num-");
        try
        {
            CreateWithAce(path, [
                "CREATE TABLE P (A LONG, CONSTRAINT PKP PRIMARY KEY (A))",
                "CREATE TABLE C1 (B LONG)",
                "CREATE TABLE C2 (B LONG)",
                "CREATE TABLE C3 (B LONG)",
            ]);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.AddForeignKey("C1", Fk("FK1", "P"));
                db.AddForeignKey("C2", Fk("FK2", "P"));
                Assert.True(db.DropConstraint("C1", "FK1"));    // leaves a gap below the max index_num
                db.AddForeignKey("C3", Fk("FK3", "P"));
            }

            // ACE reading the parent proves the two surviving incoming blocks are not claiming one number.
            using var connection = AceTestDatabase.Open(path);
            Exec(connection, "INSERT INTO P (A) VALUES (1)");
            Exec(connection, "INSERT INTO C2 (B) VALUES (1)");
            Exec(connection, "INSERT INTO C3 (B) VALUES (1)");
            using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM P INNER JOIN C3 ON P.A = C3.B";
            Assert.Equal(1, Convert.ToInt32(count.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static RelationshipSpec Fk(string name, string parent) =>
        new(name, parent, [("B", "A")], IsEnforced: true, CascadeUpdate: false, CascadeDelete: false);

    private static string CreateWithAce(string path, string[] ddl)
    {
        JetDatabase.Create(path);
        using var connection = AceTestDatabase.Open(path);
        foreach (string sql in ddl) Exec(connection, sql);
        return path;
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}