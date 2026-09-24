using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Every non-numeric column inherits the DATABASE's collating order — the one on page 0 — because that order
// decides how its index keys are encoded. A column written under a different order than its siblings is a
// mixed-collation table Access never produces, and its keys sort by rules the rest of the file does not use.
//
// The order reaches a column through whoever is writing it, and "whoever" is a TableCreator. Left unstated it
// used to fall back to General-Legacy, which is right only for a database that happens to use General-Legacy:
// on a General (v1) database every DDL path that did not pass the order explicitly wrote v0 columns into a v1
// file. CREATE TABLE and ADD COLUMN passed it; ALTER COLUMN did not, and it rebuilds the whole table.
public class ColumnCollationInheritanceTests
{
    private static string V1Database(string prefix)
    {
        string path = TemporaryDatabase.CreatePath(prefix);
        DatabaseCreator.CreateEmpty(path, collation: Collation.General);   // the Access 2010+ "General" order
        return path;
    }

    private static List<(string Name, Collation Collation)> TextColumns(JetDatabase db, string table) =>
        [.. db.Catalog.FindTable(table)!.Columns
            .Where(c => c.Type == JetDataType.Text)
            .Select(c => (c.Name, c.Collation))];

    [Fact]
    public void Every_ddl_path_writes_the_databases_order_into_the_columns_it_creates()
    {
        string path = V1Database("collation-ddl-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            Assert.Equal(Collation.General, db.Collation);   // the file really is v1

            db.CreateTable("T",
            [
                new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                new ColumnSpec("A", JetDataType.Text, 100, IsFixedLength: false),
                new ColumnSpec("B", JetDataType.Text, 100, IsFixedLength: false),
            ], primaryKey: ["Id"]);
            Assert.All(TextColumns(db, "T"), c => Assert.Equal(db.Collation, c.Collation));

            db.AddColumn("T", new ColumnSpec("C", JetDataType.Text, 100, IsFixedLength: false));
            Assert.All(TextColumns(db, "T"), c => Assert.Equal(db.Collation, c.Collation));

            // The rebuild path: retyping one column recreates every column of the table, and each of them
            // has to come back under the order the file uses — the retyped one included.
            db.AlterColumn("T", "Id", new ColumnSpec("Id", JetDataType.Text, 40, IsFixedLength: false));
            Assert.All(TextColumns(db, "T"), c => Assert.Equal(db.Collation, c.Collation));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The in-place retype writes the target descriptor's locale union itself, so it needs the order too.
    [Fact]
    public void An_in_place_retype_writes_the_databases_order()
    {
        string path = V1Database("collation-inplace-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("T",
            [
                new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                new ColumnSpec("N", JetDataType.Int32, 4, IsFixedLength: true),
            ], primaryKey: ["Id"]);

            db.AlterColumnTypeInPlace("T", "N", new ColumnSpec("N", JetDataType.Text, 60, IsFixedLength: false));
            db.Catalog.Invalidate();

            ColumnDef retyped = db.Catalog.FindTable("T")!.FindColumn("N")!;
            Assert.Equal(db.Collation, retyped.Collation);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
