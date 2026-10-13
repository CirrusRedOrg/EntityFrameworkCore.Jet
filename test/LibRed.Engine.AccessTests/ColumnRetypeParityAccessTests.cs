using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

// ALTER COLUMN retypes a column in place, Memo/OLE and a text length included (page-02b-columns): the target's
// descriptor is edited, every row re-laid, the indexes over it rebuilt in name order into the slots they held, and a
// column becoming or ceasing to be a long value gets or loses its §3.3.2 maps as ADD and DROP COLUMN give and take
// them. ACE and LibRed run the same ALTER on copies of one file and must leave the same
// file: the same catalog and rows, and the same bytes on every page but MSysObjects' own (its DateUpdate is the
// clock) — and, when a Memo is re-declared as a Memo, the dead space below the live records on the table's
// usage-map page, where ACE leaves stale copies of the records it retired. The INSERT and UPDATE cases are rows written
// over the same dead fixed slots, dropped variable chunks and short rows a retype meets.
[Collection(AceCollection.Name)]
public class ColumnRetypeParityAccessTests
{
    private static readonly string[] Values = ["short", new string('a', 31), new string('b', 32), new string('c', 33), new string('d', 200)];

    // Each table's history before the statement under test: rows written before, between and after ADD and DROP
    // COLUMN, so its rows disagree about their fixed region and whether they carry a variable trailer.
    private static readonly string[] Histories =
    [
        // The highest column id, and the only variable column, dropped: rows from before and after the drop.
        "CREATE TABLE D (ID LONG PRIMARY KEY, X LONG, S TEXT(10))",
        "INSERT INTO D VALUES (1, 1, 'one')",
        "INSERT INTO D VALUES (2, NULL, NULL)",
        "ALTER TABLE D DROP COLUMN S",
        "INSERT INTO D VALUES (3, 3)",
        // S, then a fixed Y above it, both dropped: the count-3 row's trailer is read as fixed data, as ACE reads it.
        "CREATE TABLE D4 (ID LONG PRIMARY KEY, X LONG)",
        "INSERT INTO D4 VALUES (1, 1)",
        "ALTER TABLE D4 ADD COLUMN S TEXT(10)",
        "INSERT INTO D4 VALUES (2, 2, 'two')",
        "ALTER TABLE D4 ADD COLUMN Y LONG",
        "INSERT INTO D4 VALUES (3, 3, 'six', 7)",
        "ALTER TABLE D4 DROP COLUMN S",
        "ALTER TABLE D4 DROP COLUMN Y",
        "INSERT INTO D4 VALUES (4, 4)",
        // S1 dropped below a live S2: the live column's 0x07 says a row from S1's time has a trailer.
        "CREATE TABLE D6 (ID LONG PRIMARY KEY, X LONG)",
        "INSERT INTO D6 VALUES (1, 1)",
        "ALTER TABLE D6 ADD COLUMN S1 TEXT(10)",
        "INSERT INTO D6 VALUES (2, 2, 'two')",
        "ALTER TABLE D6 ADD COLUMN S2 TEXT(10)",
        "INSERT INTO D6 VALUES (3, 3, 'six', 'ten')",
        "ALTER TABLE D6 DROP COLUMN S1",
        "INSERT INTO D6 VALUES (4, 4, 'end')",
        // S dropped below a live fixed W: rows predating W get its slot from their old bytes.
        "CREATE TABLE DW (ID LONG PRIMARY KEY, X LONG)",
        "INSERT INTO DW VALUES (1, 1)",
        "ALTER TABLE DW ADD COLUMN S TEXT(10)",
        "INSERT INTO DW VALUES (2, 2, 'two')",
        "ALTER TABLE DW ADD COLUMN W LONG",
        "INSERT INTO DW VALUES (3, 3, 'six', 3)",
        "ALTER TABLE DW DROP COLUMN S",
        "INSERT INTO DW VALUES (4, 4, 4)",
        // A trailer-less row whose dead Y/Z bytes spell a valid trailer (numVar 1, chunk 10..12), which ACE ignores.
        "CREATE TABLE DM (ID LONG PRIMARY KEY, X LONG, Y LONG, Z LONG)",
        "INSERT INTO DM VALUES (1, 1, 786432, 65546)",
        "INSERT INTO DM VALUES (2, 2, 5, 6)",
        "ALTER TABLE DM DROP COLUMN Y",
        "ALTER TABLE DM DROP COLUMN Z",
        "ALTER TABLE DM ADD COLUMN S TEXT(10)",
        "ALTER TABLE DM DROP COLUMN S",
        // A dead fixed slot at the end of the region: B dropped, or retyped away to TEXT.
        "CREATE TABLE RD (K LONG PRIMARY KEY, A LONG, B LONG, S TEXT(10))",
        "INSERT INTO RD VALUES (1, 1, 7, 'one')",
        "ALTER TABLE RD DROP COLUMN B",
        "CREATE TABLE RR (K LONG PRIMARY KEY, A LONG, B LONG, S TEXT(10))",
        "INSERT INTO RR VALUES (1, 1, 7, 'one')",
        "ALTER TABLE RR ALTER COLUMN B TEXT(10)",
        // Rows shorter than the region: written before a fixed ADD COLUMN.
        "CREATE TABLE SU (K LONG PRIMARY KEY, A LONG, S TEXT(10))",
        "INSERT INTO SU VALUES (1, 1, 'one')",
        "INSERT INTO SU VALUES (3, 3, NULL)",
        "ALTER TABLE SU ADD COLUMN B LONG",
        // All-fixed rows shorter than the region by more than their own length: what fills the slots past them.
        "CREATE TABLE SF (K LONG PRIMARY KEY, A LONG)",
        "INSERT INTO SF VALUES (1, 1)",
        "INSERT INTO SF VALUES (2, 2)",
        "ALTER TABLE SF ADD COLUMN B LONG",
        "ALTER TABLE SF ADD COLUMN C LONG",
        // Dates and numbers, retyped into each other: a date becomes its serial, a number the date at that serial.
        "CREATE TABLE DN (K LONG PRIMARY KEY, D DATETIME, N DOUBLE)",
        "INSERT INTO DN VALUES (1, #2020-01-02 18:00:00#, 1000)",
        "INSERT INTO DN VALUES (2, #1899-12-30 06:00:00#, -1.5)",
        "INSERT INTO DN VALUES (3, NULL, 0)",
        // A dropped variable column's chunk and dead bit.
        "CREATE TABLE DV (K LONG PRIMARY KEY, A LONG, S1 TEXT(10), S2 TEXT(10))",
        "INSERT INTO DV VALUES (1, 1, 'one', 'uno')",
        "INSERT INTO DV VALUES (2, 2, 'two', 'dos')",
        "ALTER TABLE DV DROP COLUMN S1",
    ];

    [Theory]
    [InlineData("T", "ALTER TABLE T ALTER COLUMN B MEMO")]            // Text -> Memo: short values inline, long on a page
    [InlineData("M", "ALTER TABLE M ALTER COLUMN M TEXT(255)")]       // Memo -> Text: the maps retired, the page released
    [InlineData("M", "ALTER TABLE M ALTER COLUMN M MEMO")]            // Memo re-declared: an id burned, maps replaced
    [InlineData("T", "ALTER TABLE T ALTER COLUMN X DOUBLE")]          // a fixed retype over a NULL: the dead bit and slot
    [InlineData("T", "ALTER TABLE T ALTER COLUMN B TEXT(200)")]       // a text narrowing: an id burned, every row re-laid
    [InlineData("I", "ALTER TABLE I ALTER COLUMN E TEXT(100)")]       // a text widening, under one index
    [InlineData("I", "ALTER TABLE I ALTER COLUMN B TEXT(20)")]        // two indexes whose names run against their slots
    [InlineData("I", "ALTER TABLE I ALTER COLUMN ID DOUBLE")]         // the primary key and IX_A: IX_A takes slot 0
    [InlineData("I", "ALTER TABLE I ALTER COLUMN B MEMO")]            // the two indexes again, to Memo
    [InlineData("T", "ALTER TABLE T ALTER COLUMN X YESNO")]           // to Yes/No: the value goes to the null bitmap
    [InlineData("N", "ALTER TABLE N ALTER COLUMN S YESNO")]           // numeric text to Yes/No, under an index
    [InlineData("D", "ALTER TABLE D ALTER COLUMN X DOUBLE")]          // after its highest id, its only variable column, is dropped
    [InlineData("D4", "ALTER TABLE D4 ALTER COLUMN X DOUBLE")]
    [InlineData("D6", "ALTER TABLE D6 ALTER COLUMN X DOUBLE")]
    [InlineData("DW", "ALTER TABLE DW ALTER COLUMN X DOUBLE")]
    [InlineData("DM", "ALTER TABLE DM ALTER COLUMN X DOUBLE")]
    [InlineData("RD", "ALTER TABLE RD ALTER COLUMN A DOUBLE")]        // the new slot lands on the dead one
    [InlineData("RR", "ALTER TABLE RR ALTER COLUMN A DOUBLE")]
    [InlineData("SU", "ALTER TABLE SU ALTER COLUMN A DOUBLE")]        // short rows grow to the new slot
    [InlineData("SU", "ALTER TABLE SU ALTER COLUMN B TEXT(10)")]      // short rows, a variable target
    [InlineData("RD", "INSERT INTO RD (K, A) VALUES (2, 2)")]         // a new row has no dead slot
    [InlineData("RD", "UPDATE RD SET A = 9 WHERE K = 1")]             // an updated row keeps its own
    [InlineData("RR", "UPDATE RR SET S = 'longer!!' WHERE K = 1")]
    [InlineData("SU", "UPDATE SU SET A = 9 WHERE K = 1")]             // a short row grows to the live region
    [InlineData("SU", "UPDATE SU SET B = 7 WHERE K = 3")]
    [InlineData("SF", "UPDATE SF SET A = A + 10")]                    // rows grown past their old record's end
    [InlineData("SF", "ALTER TABLE SF ALTER COLUMN A DOUBLE")]
    [InlineData("DV", "UPDATE DV SET S2 = 'longer!!' WHERE K = 1")]   // the dropped chunk and its bit stay
    [InlineData("DV", "UPDATE DV SET S2 = NULL, A = NULL WHERE K = 2")]
    [InlineData("D4", "UPDATE D4 SET X = 9 WHERE ID = 2")]            // the trailer ACE reads as fixed data
    [InlineData("SF", "ALTER TABLE SF ADD COLUMN Y YESNO")]           // an added Yes/No has no fixed slot: offset 0
    [InlineData("RD", "ALTER TABLE RD ADD COLUMN Y YESNO")]
    [InlineData("RD", "ALTER TABLE RD ADD COLUMN Z LONG")]            // a column added over a dead slot at the end
    [InlineData("RR", "ALTER TABLE RR ADD COLUMN Z DOUBLE")]
    [InlineData("RD", "ALTER TABLE RD ADD COLUMN Z BYTE")]            // narrower than the dead slot it lands on
    [InlineData("DN", "ALTER TABLE DN ALTER COLUMN D LONG")]          // a date into a number is its serial
    [InlineData("DN", "ALTER TABLE DN ALTER COLUMN D DOUBLE")]
    [InlineData("DN", "ALTER TABLE DN ALTER COLUMN D YESNO")]
    [InlineData("DN", "ALTER TABLE DN ALTER COLUMN N DATETIME")]      // a number into a date the date at that serial
    public void A_statement_leaves_the_file_ACE_leaves(string table, string alter)
    {
        string basePath = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "retype-base-");
        string acePath = TemporaryDatabase.CopyPath(basePath, "retype-ace-");
        string libredPath = TemporaryDatabase.CopyPath(basePath, "retype-libred-");
        try
        {
            using (OleDbConnection c = AceTestDatabase.Open(acePath))
            {
                Exec(c, "CREATE TABLE T (ID LONG PRIMARY KEY, B TEXT(255), X LONG)");
                Exec(c, "CREATE TABLE M (ID LONG PRIMARY KEY, M MEMO, X LONG)");
                for (int i = 0; i < Values.Length; i++)
                    foreach (string t in (string[])["T", "M"])
                        Exec(c, $"INSERT INTO {t} VALUES ({i + 1}, '{Values[i]}', {i + 1})");
                Exec(c, "INSERT INTO T VALUES (9, NULL, 9)");
                Exec(c, "INSERT INTO M VALUES (9, NULL, 9)");
                Exec(c, "INSERT INTO T VALUES (10, 'z', NULL)");
                // Indexes over B in slots 1 and 3, out of name order, with IX_E between them; IX_Y shares IX_Z's.
                Exec(c, "CREATE TABLE I (ID LONG CONSTRAINT PK_I PRIMARY KEY, B TEXT(50), E TEXT(50))");
                Exec(c, "CREATE INDEX IX_Z ON I (B)");
                Exec(c, "CREATE INDEX IX_Y ON I (B)");
                Exec(c, "CREATE INDEX IX_E ON I (E)");
                Exec(c, "CREATE UNIQUE INDEX IX_A ON I (B, ID)");
                for (int i = 0; i < Values.Length; i++)
                    Exec(c, $"INSERT INTO I VALUES ({i + 1}, 'b{Values.Length - i}', 'e{i}')");
                Exec(c, "INSERT INTO I VALUES (9, NULL, NULL)");
                // Text that reads as a number, under an index, for a retype to Yes/No.
                Exec(c, "CREATE TABLE N (ID LONG PRIMARY KEY, S TEXT(10))");
                Exec(c, "CREATE INDEX IX_S ON N (S)");
                string[] numbers = ["'5'", "'0'", "'2.5'", "'(5)'", "NULL"];
                for (int i = 0; i < numbers.Length; i++)
                    Exec(c, $"INSERT INTO N VALUES ({i + 1}, {numbers[i]})");
                foreach (string statement in Histories)
                    Exec(c, statement);
            }
            OleDbConnection.ReleaseObjectPool();
            File.Copy(acePath, libredPath, overwrite: true);

            using (OleDbConnection c = AceTestDatabase.Open(acePath)) Exec(c, alter);
            OleDbConnection.ReleaseObjectPool();
            JetFormatBase format;
            using (var db = JetDatabase.Open(libredPath, readOnly: false))
            {
                format = db.Format;
                new QueryEngine(db).ExecuteNonQuery(alter);
            }

            Assert.Equal(Describe(acePath, table), Describe(libredPath, table));

            byte[] ace = File.ReadAllBytes(acePath), libred = File.ReadAllBytes(libredPath);
            Assert.Equal(ace.Length, libred.Length);
            (int catalogPage, int usageMapPage, int lowestRecord) = Landmarks(acePath, table);
            int pageSize = format.PageSize;
            for (int page = 1; page < ace.Length / pageSize; page++)
            {
                var differ = Enumerable.Range(0, pageSize).Where(i => ace[page * pageSize + i] != libred[page * pageSize + i]).ToList();
                if (differ.Count == 0 || (int)DataPage.ReadOwner(ace.AsSpan(page * pageSize, pageSize), format) == catalogPage) continue;
                Assert.True(page == usageMapPage && differ.All(i => i < lowestRecord),
                    $"page {page} differs at 0x{differ[0]:X3} ({differ.Count} bytes)");
            }
        }
        finally
        {
            TemporaryDatabase.Delete(basePath);
            TemporaryDatabase.Delete(acePath);
            TemporaryDatabase.Delete(libredPath);
        }
    }

    // The table's columns (descriptors whole), long-value maps, and each row's raw record and value.
    private static string Describe(string path, string tableName)
    {
        var lines = new List<string>();
        using var db = JetDatabase.Open(path, readOnly: true);
        TableDefinition t = db.Catalog.FindTable(tableName)!;
        var tdef = db.ReadTableDefinition(t.DefinitionPage);
        lines.AddRange(t.Columns.Select(c => $"{c.Name} {c.Type} id {c.ColumnId} {Convert.ToHexString(c.RawDescriptor ?? [])}"));
        lines.AddRange(tdef.LongValueOwnedMaps.Select(m => $"maps {m.Key}: {m.Value} {tdef.LongValueFreeMaps[m.Key]}"));
        var table = db.OpenTable(tableName);
        var reader = new RowInserter(table.Channel, t);
        foreach ((RowId id, object?[] values) in table.Rows().WithIds())
            lines.Add($"{id.Page}:{id.Row} {Convert.ToHexString(reader.ReadRow(id))} {string.Join("|", values.Select(v => v?.ToString() ?? "null"))}");
        return string.Join("\n", lines);
    }

    // MSysObjects' definition page (its data pages carry it as owner), the table's usage-map page, and the offset of
    // the lowest live record on that page.
    private static (int CatalogPage, int UsageMapPage, int LowestRecord) Landmarks(string path, string tableName)
    {
        using var db = JetDatabase.Open(path, readOnly: true);
        TableDefinition t = db.Catalog.FindTable(tableName)!;
        byte[] file = File.ReadAllBytes(path);
        int pageSize = db.Format.PageSize;
        int holder = PageBuffer.ReadRecordPointer(file, t.DefinitionPage * pageSize + db.Format.TdefOwnedPagesOffset).Page;
        int rows = DataPage.ReadRowCount(file.AsSpan(holder * pageSize, pageSize), db.Format);
        int lowest = Enumerable.Range(0, rows)
            .Select(i => DataPage.ReadSlot(file.AsSpan(holder * pageSize, pageSize), db.Format, i))
            .Where(s => (s.Flags & RowSlotFlags.Deleted) == 0).Min(s => s.Offset);
        return (db.Catalog.FindTable("MSysObjects")!.DefinitionPage, holder, lowest);
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}