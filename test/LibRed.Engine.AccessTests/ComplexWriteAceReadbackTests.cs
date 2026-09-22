using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// LibRed inserting into and deleting from a table that has complex (multi-value / attachment) columns, with
/// ACE asked whether it still accepts the result. Until this worked both statements were refused outright —
/// the index key encoding for a Complex column threw — so the point of the test is that lifting that refusal
/// did not start producing files ACE disagrees with.
/// </summary>
[Collection(AceCollection.Name)]
public class ComplexWriteAceReadbackTests(ITestOutputHelper output)
{
    private const string Source = @"D:\exampleaccdb\complex1.accdb";

    [Fact]
    public void Ace_reads_back_a_table_libred_inserted_into_and_deleted_from()
    {
        if (!File.Exists(Source)) { output.WriteLine($"Skipped: {Source} not present."); return; }
        string path = TemporaryDatabase.CopyPath(Source, "complex-write-");
        try
        {
            int inserted, deletedRecordId, complexColumnCount;
            var idsBefore = new List<int>();

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                TableDef table = db.Catalog.FindTable("Table1")!;
                ColumnDef idColumn = table.Columns[0];
                complexColumnCount = db.Catalog.ComplexColumns.Count(c => c.OwnerTable.Name == "Table1");

                // A record that owns values — the one whose deletion has to cascade.
                deletedRecordId = db.Catalog.ComplexColumns
                    .Where(c => c.OwnerTable.Name == "Table1")
                    .SelectMany(c => db.OpenTable(c.FlatTable.Name).Rows()
                        .Select(r => Convert.ToInt32(r[c.OwnerLink.Index])))
                    .Distinct().First();

                foreach (object?[] r in db.OpenTable("Table1").Rows())
                    idsBefore.Add(Convert.ToInt32(r[idColumn.Index]));

                inserted = engine.ExecuteNonQuery("INSERT INTO Table1 (Col1) VALUES ('libred')");
                engine.ExecuteNonQuery(
                    $"DELETE FROM Table1 WHERE [{idColumn.Name}] = "
                    + db.OpenTable("Table1").Rows()
                        .Where(r => Convert.ToInt32(r[table.FindColumn("Attachment")!.Index]) == deletedRecordId)
                        .Select(r => Convert.ToInt32(r[idColumn.Index]))
                        .First());
            }
            Assert.Equal(1, inserted);

            using (var db = JetDatabase.Open(path))
            {
                // Every complex column of the table lost that record's values.
                foreach (ComplexColumn c in db.Catalog.ComplexColumns.Where(c => c.OwnerTable.Name == "Table1"))
                {
                    Assert.Empty(db.ReadComplexValues(c, deletedRecordId));
                    output.WriteLine($"{c.ColumnName}: no values remain for record {deletedRecordId}");
                }

                // The new row took one complex id, shared by every complex column.
                TableDef table = db.Catalog.FindTable("Table1")!;
                object?[] newest = db.OpenTable("Table1").Rows()
                    .Last(r => !idsBefore.Contains(Convert.ToInt32(r[table.Columns[0].Index])));
                var ids = db.Catalog.ComplexColumns.Where(c => c.OwnerTable.Name == "Table1")
                    .Select(c => (int)newest[table.FindColumn(c.ColumnName)!.Index]!)
                    .Distinct().ToList();
                int shared = Assert.Single(ids);
                output.WriteLine($"new row carries complex id {shared} in all {complexColumnCount} complex columns");
            }

            // The real question: does ACE still accept the file?
            using OleDbConnection ace = AceTestDatabase.Open(path);
            using OleDbCommand count = ace.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM Table1";
            int rows = Convert.ToInt32(count.ExecuteScalar());
            output.WriteLine($"ACE reads {rows} rows from Table1 (was {idsBefore.Count}, +1 insert, -1 delete)");
            Assert.Equal(idsBefore.Count, rows);

            using OleDbCommand readAll = ace.CreateCommand();
            readAll.CommandText = "SELECT Col1 FROM Table1";
            using OleDbDataReader reader = readAll.ExecuteReader();
            var values = new List<string?>();
            while (reader.Read()) values.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
            Assert.Contains("libred", values);
            output.WriteLine($"ACE sees Col1 = [{string.Join(", ", values)}]");
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
