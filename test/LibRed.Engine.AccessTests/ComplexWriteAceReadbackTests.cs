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

    // A complex column is held to its values by three links: its descriptor's 0x0B carries the
    // MSysComplexColumns key (page-02b §3.4), where an ordinary column carries the collation LANGID; that
    // catalog row names the owning table by TDEF page; and the values sit in an f_<GUID> flat table keyed to
    // the row. A retype that falls back to the drop-and-recreate rebuild rewrites every descriptor and moves
    // the table to a new TDEF page, carrying none of the three — it left complex1's Table1 with no complex
    // columns at all. ACE performs the same ALTER with every one of them intact, so LibRed's refusal is a
    // measured GAP, recorded here: the test asserts the refusal, and that nothing was destroyed on the way.
    [Theory]
    [InlineData(@"D:\exampleaccdb\complex1.accdb", "Table1", "Col1", "LONGTEXT")]
    [InlineData(@"D:\exampleaccdb\LIBRARY.accdb", "Book", "BK_publisher", "LONGTEXT")]
    public void An_alter_of_another_column_keeps_the_complex_columns_links(
        string source, string table, string column, string newType)
    {
        if (!File.Exists(source)) { output.WriteLine($"Skipped: {source} not present."); return; }
        // What ACE itself does with the same statement is the oracle for what LibRed should do.
        string acePath = TemporaryDatabase.CopyPath(source, "complex-alter-ace-");
        string aceOutcome;
        using (OleDbConnection connection = AceTestDatabase.Open(acePath))
        using (OleDbCommand alter = connection.CreateCommand())
        {
            alter.CommandText = $"ALTER TABLE [{table}] ALTER COLUMN [{column}] {newType}";
            try { alter.ExecuteNonQuery(); aceOutcome = "accepted"; }
            catch (OleDbException e) { aceOutcome = e.Message.Trim(); }
        }
        output.WriteLine($"ACE: {aceOutcome}");
        if (aceOutcome == "accepted")
            output.WriteLine($"ACE after: {Remains(acePath)}");

        string path = TemporaryDatabase.CopyPath(source, "complex-alter-");
        try
        {
            string before = Remains(path);
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var refused = Assert.Throws<NotSupportedException>(() =>
                    new QueryEngine(db).ExecuteNonQuery($"ALTER TABLE [{table}] ALTER COLUMN [{column}] {newType}"));
                output.WriteLine($"LibRed: {refused.Message}");
                Assert.Contains("multi-value or attachment", refused.Message, StringComparison.Ordinal);
            }

            // Refused, and refused before touching anything.
            output.WriteLine($"before: {before}");
            output.WriteLine($"after:  {Remains(path)}");
            Assert.Equal(before, Remains(path));

            // And ACE still reads the table.
            using OleDbConnection ace = AceTestDatabase.Open(path);
            using OleDbCommand count = ace.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM [{table}]";
            output.WriteLine($"ACE reads {Convert.ToInt32(count.ExecuteScalar())} rows");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Dropping a table that owns complex columns. Each one has a row in MSysComplexColumns and a backing
    // f_<GUID> flat table holding its values, neither of which the table's own pages account for — so what
    // ACE takes with the table, LibRed has to take too, or the file keeps catalog rows pointing at a table
    // that is gone and flat tables nothing will ever read.
    [Fact]
    public void Dropping_a_table_with_complex_columns_leaves_what_ace_leaves()
    {
        if (!File.Exists(Source)) { output.WriteLine($"Skipped: {Source} not present."); return; }

        string ace = TemporaryDatabase.CopyPath(Source, "complex-drop-ace-");
        using (OleDbConnection connection = AceTestDatabase.Open(ace))
        using (OleDbCommand drop = connection.CreateCommand())
        {
            drop.CommandText = "DROP TABLE Table1";
            drop.ExecuteNonQuery();
        }

        string libred = TemporaryDatabase.CopyPath(Source, "complex-drop-lib-");
        using (var db = JetDatabase.Open(libred, readOnly: false))
            new QueryEngine(db).ExecuteNonQuery("DROP TABLE Table1");

        string aceLeft = Remains(ace), libredLeft = Remains(libred);
        output.WriteLine($"ACE:    {aceLeft}");
        output.WriteLine($"LibRed: {libredLeft}");
        Assert.Equal(aceLeft, libredLeft);
    }

    // And dropping one complex column rather than the whole table: the same row and flat table have to go, and
    // whether ACE's SQL will even do it is the first question.
    [Fact]
    public void Dropping_a_complex_column_leaves_what_ace_leaves()
    {
        if (!File.Exists(Source)) { output.WriteLine($"Skipped: {Source} not present."); return; }

        string ace = TemporaryDatabase.CopyPath(Source, "complex-dropcol-ace-");
        string? refusal = null;
        using (OleDbConnection connection = AceTestDatabase.Open(ace))
        using (OleDbCommand drop = connection.CreateCommand())
        {
            drop.CommandText = "ALTER TABLE Table1 DROP COLUMN att2";
            try { drop.ExecuteNonQuery(); }
            catch (OleDbException e) { refusal = e.Message.Trim(); }
        }
        output.WriteLine($"ACE: {refusal ?? "accepted"}");

        string libred = TemporaryDatabase.CopyPath(Source, "complex-dropcol-lib-");
        Exception? libredRefusal = null;
        using (var db = JetDatabase.Open(libred, readOnly: false))
        {
            try { new QueryEngine(db).ExecuteNonQuery("ALTER TABLE Table1 DROP COLUMN att2"); }
            catch (Exception e) { libredRefusal = e; }
        }
        output.WriteLine($"LibRed: {libredRefusal?.Message ?? "accepted"}");

        Assert.Equal(refusal is null, libredRefusal is null);
        if (refusal is not null) return;

        string aceLeft = Remains(ace, "Table1"), libredLeft = Remains(libred, "Table1");
        output.WriteLine($"ACE:    {aceLeft}");
        output.WriteLine($"LibRed: {libredLeft}");
        Assert.Equal(aceLeft, libredLeft);
    }

    /// <summary>What the catalog still says about complex columns: the MSysComplexColumns rows by column name,
    /// and the names of the f_&lt;GUID&gt; flat tables still present. With <paramref name="table"/>, also that
    /// table's own columns and indexes, so a drop that was accepted and did nothing is visible as such.</summary>
    private static string Remains(string path, string? table = null)
    {
        using var db = JetDatabase.Open(path);
        if (table is not null)
        {
            TableDef owner = db.Catalog.FindTable(table)!;
            string columns = string.Join(",", owner.Columns.Select(c => c.Name));
            string indexes = string.Join(",", owner.Indexes.Select(i => i.Name.Split('_')[0]).Order(StringComparer.Ordinal));
            return $"columns=[{columns}] indexes=[{indexes}] {Remains(path)}";
        }
        TableDef complexColumns = db.Catalog.FindTable("MSysComplexColumns")!;
        int name = complexColumns.FindColumn("ColumnName")!.Index;
        var rows = db.OpenTable("MSysComplexColumns").Rows()
            .Select(r => Convert.ToString(r[name]) ?? "?")
            .Order(StringComparer.Ordinal);
        var flat = db.Catalog.Tables
            .Where(t => t.Name.StartsWith("f_", StringComparison.Ordinal))
            .Select(t => t.Name[^12..])   // the trailing _<column> part, which the GUID prefix buries
            .Order(StringComparer.Ordinal);
        return $"MSysComplexColumns=[{string.Join(",", rows)}] flat=[{string.Join(",", flat)}]";
    }

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
