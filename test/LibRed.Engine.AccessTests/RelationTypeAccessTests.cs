using System.Reflection;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// A one-to-one relationship is the grbit bit 0x01, which only Access's dialog and DAO set (SQL cannot). LibRed
// reports it as DAO does — RELATION_TYPE "ONE" — whether or not the relationship is enforced; an unenforced one
// has no child index for anything to be inferred from.
[Collection(AceCollection.Name)]
public class RelationTypeAccessTests
{
    private const int UseJet = 2, Ace12 = 128, OneToOne = 1, DontEnforce = 2;

    [Fact]
    public void A_one_to_one_created_by_dao_reads_as_one_enforced_or_not()
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not available in this process.");
        object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", UseJet)!;

        string path = TemporaryDatabase.CreatePath("reltype-", ".accdb");
        try
        {
            object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", Ace12)!;
            foreach (string sql in (string[])
                ["CREATE TABLE P (ID LONG PRIMARY KEY)", "CREATE TABLE E (ID LONG PRIMARY KEY)", "CREATE TABLE U (ID LONG PRIMARY KEY)"])
                Invoke(database, "Execute", sql, 128);
            Relate(database, "enforced_one", "E", OneToOne);
            Relate(database, "unenforced_one", "U", OneToOne | DontEnforce);
            Invoke(database, "Close");

            using var db = JetDatabase.Open(path, readOnly: true);
            Assert.True(db.Catalog.Relationships.Single(r => r.Name == "enforced_one").IsOneToOne);
            Assert.True(db.Catalog.Relationships.Single(r => r.Name == "unenforced_one").IsOneToOne);

            var types = new QueryEngine(db)
                .ExecuteQuery("SELECT `RELATION_NAME`, `RELATION_TYPE` FROM `INFORMATION_SCHEMA.RELATIONS`")
                .Rows.ToDictionary(r => (string)r[0]!, r => (string)r[1]!);
            Assert.Equal("ONE", types["enforced_one"]);
            Assert.Equal("ONE", types["unenforced_one"]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // DAO: CreateRelation(name, primary table, foreign table, attributes); Field.Name = primary field,
    // Field.ForeignName = foreign field.
    private static void Relate(object database, string name, string child, int attributes)
    {
        object relation = Invoke(database, "CreateRelation", name, "P", child, attributes)!;
        object field = Invoke(relation, "CreateField", "ID")!;
        field.GetType().InvokeMember("ForeignName", BindingFlags.SetProperty, null, field, ["ID"]);
        object fields = relation.GetType().InvokeMember("Fields", BindingFlags.GetProperty, null, relation, null)!;
        Invoke(fields, "Append", field);
        object relations = database.GetType().InvokeMember("Relations", BindingFlags.GetProperty, null, database, null)!;
        Invoke(relations, "Append", relation);
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
}
