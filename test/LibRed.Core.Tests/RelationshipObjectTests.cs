using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Every relationship is also a type-8 <c>MSysObjects</c> object in the Relationships container, with two
/// <c>MSysACEs</c> rows, as ACE records it (docs/format/system-catalog.md).
/// </summary>
public class RelationshipObjectTests : TempDatabaseTest
{
    private static readonly RelationshipSpec Fk = new("fkCP", "P", [("PId", "Id")], IsEnforced: true, CascadeUpdate: false, CascadeDelete: false);

    private static string Database()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "relobj-");
        using var db = JetDatabase.Open(path, readOnly: false);
        db.CreateTable("P", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
        db.CreateTable("C", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true), new ColumnSpec("PId", JetDataType.Int32, 4, IsFixedLength: true)]);
        return path;
    }

    /// <summary>MSysObjects rows as column-name dictionaries.</summary>
    private static List<Dictionary<string, object?>> Objects(JetDatabase db) => Rows(db, "MSysObjects");

    private static List<Dictionary<string, object?>> Rows(JetDatabase db, string table)
    {
        IReadOnlyList<ColumnDef> columns = db.Catalog.FindTable(table)!.Columns;
        return db.OpenTable(table).Rows().Select(r => columns.ToDictionary(c => c.Name, c => r[c.Index])).ToList();
    }

    [Fact]
    public void A_relationship_is_recorded_as_a_type_8_object_with_two_permission_rows()
    {
        string path = Database();
        int highest;
        using (var db = JetDatabase.Open(path, readOnly: false))
        {
            highest = Objects(db).Select(o => Convert.ToInt32(o["Id"])).Where(id => id < 0).Max();
            db.AddForeignKey("C", Fk);
        }

        using var check = JetDatabase.Open(path);
        Dictionary<string, object?> fk = Objects(check).Single(o => Equals(o["Name"], "fkCP"));
        Assert.Equal((short)8, fk["Type"]);
        Assert.Equal(0x0F000003, fk["ParentId"]);
        Assert.Equal(0, fk["Flags"]);
        Assert.Equal(new byte[] { 0x69, 0x0C }, fk["Owner"]);
        Assert.Equal(highest + 1, fk["Id"]);
        Assert.Null(fk["LvProp"]);

        var aces = Rows(check, "MSysACEs").Where(a => Equals(a["ObjectId"], fk["Id"])).ToList();
        Assert.Equal(2, aces.Count);
        Assert.Equal(0xF00FE, aces.Single(a => ((byte[])a["SID"]!)[0] == 0x69)["ACM"]);
        Assert.Equal(0xFFFFF, aces.Single(a => ((byte[])a["SID"]!)[0] == 0x68)["ACM"]);
    }

    [Fact]
    public void Dropping_the_relationship_removes_its_object_and_the_next_one_takes_its_id()
    {
        string path = Database();
        using var db = JetDatabase.Open(path, readOnly: false);
        db.AddForeignKey("C", Fk);
        int id = Convert.ToInt32(Objects(db).Single(o => Equals(o["Name"], "fkCP"))["Id"]);

        Assert.True(db.DropConstraint("C", "fkCP"));
        Assert.DoesNotContain(Objects(db), o => Equals(o["Name"], "fkCP"));
        Assert.DoesNotContain(Rows(db, "MSysACEs"), a => Equals(a["ObjectId"], id));

        db.AddForeignKey("C", Fk with { Name = "fkAgain" });
        Assert.Equal(id, Objects(db).Single(o => Equals(o["Name"], "fkAgain"))["Id"]);
    }

    [Fact]
    public void Dropping_the_referencing_table_removes_the_relationship_object()
    {
        string path = Database();
        using var db = JetDatabase.Open(path, readOnly: false);
        db.AddForeignKey("C", Fk);

        Assert.True(db.DropTable("C"));
        Assert.DoesNotContain(Objects(db), o => Equals(o["Name"], "fkCP"));
    }

    [Fact]
    public void A_relationship_may_share_a_tables_name_but_not_another_relationships()
    {
        string path = Database();
        using var db = JetDatabase.Open(path, readOnly: false);
        db.AddForeignKey("C", Fk with { Name = "P" });
        Assert.Contains(Objects(db), o => Equals(o["Name"], "P") && Equals(o["Type"], (short)8));

        db.CreateTable("D", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true), new ColumnSpec("PId", JetDataType.Int32, 4, IsFixedLength: true)]);
        var refused = Assert.Throws<SchemaObjectExistsException>(() => db.AddForeignKey("D", Fk with { Name = "P" }));
        Assert.Equal("There is already a relationship named 'P' in the current database.", refused.Message);
    }
}
