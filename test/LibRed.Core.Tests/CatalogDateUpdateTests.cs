using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A table's <c>MSysObjects.DateUpdate</c> moves whenever its definition changes, and its <c>DateCreate</c> never
/// does — as ACE does it, measured against ACE: an index created on the table, and a new table referencing it, both
/// move the table's <c>DateUpdate</c>.
/// </summary>
public class CatalogDateUpdateTests
{
    [Fact]
    public void Creating_an_index_moves_the_tables_date_update()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "dateupd-ix-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("T", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                                 new ColumnSpec("X", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
            (DateTime created, DateTime updated) before = Dates(db, "T");

            Thread.Sleep(50);
            db.CreateIndex("T", "ix", [("X", false)]);

            (DateTime created, DateTime updated) after = Dates(db, "T");
            Assert.Equal(before.created, after.created);
            Assert.True(after.updated > before.updated, $"DateUpdate stayed at {before.updated:HH:mm:ss.fff}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_table_referencing_another_moves_the_parents_date_update()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "dateupd-fk-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("P", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
            (DateTime created, DateTime updated) before = Dates(db, "P");

            Thread.Sleep(50);
            db.CreateTable("C",
                [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                 new ColumnSpec("Pid", JetDataType.Int32, 4, IsFixedLength: true)],
                primaryKey: ["Id"],
                relationships: [new RelationshipSpec("fk", "P", [("Pid", "Id")], true, CascadeUpdate: false, CascadeDelete: false)]);

            (DateTime created, DateTime updated) after = Dates(db, "P");
            Assert.Equal(before.created, after.created);
            Assert.True(after.updated > before.updated, $"DateUpdate stayed at {before.updated:HH:mm:ss.fff}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static (DateTime Created, DateTime Updated) Dates(JetDatabase db, string name)
    {
        db.Catalog.Invalidate();
        var objects = db.OpenTable("MSysObjects");
        var def = objects.Definition;
        int Col(string n) => def.FindColumn(n)!.Index;
        object?[] row = objects.Rows().Single(r => (string?)r[Col("Name")] == name && (short)r[Col("Type")]! == 1);
        return ((DateTime)row[Col("DateCreate")]!, (DateTime)row[Col("DateUpdate")]!);
    }
}
