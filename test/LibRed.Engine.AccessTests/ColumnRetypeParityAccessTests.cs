using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

// ALTER COLUMN retypes a column in place, Memo/OLE and a text length included (page-02b-columns): the target's
// descriptor is edited, every row re-laid, the indexes over it rebuilt in name order into the slots they held, and a
// column becoming or ceasing to be a long value gets or loses its §3.3.2 maps as ADD and DROP COLUMN give and take
// them. ACE and LibRed run the same ALTER on copies of one file and must leave the same
// file: the same catalog and rows, and the same bytes on every page but MSysObjects' own (its DateUpdate is the
// clock) — and, when a Memo is re-declared as a Memo, the dead space below the live records on the table's
// usage-map page, where ACE leaves stale copies of the records it retired.
[Collection(AceCollection.Name)]
public class ColumnRetypeParityAccessTests
{
    private static readonly string[] Values = ["short", new string('a', 31), new string('b', 32), new string('c', 33), new string('d', 200)];

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
    public void A_retype_leaves_the_file_ACE_leaves(string table, string alter)
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
            }
            OleDbConnection.ReleaseObjectPool();
            File.Copy(acePath, libredPath, overwrite: true);

            using (OleDbConnection c = AceTestDatabase.Open(acePath)) Exec(c, alter);
            OleDbConnection.ReleaseObjectPool();
            using (var db = JetDatabase.Open(libredPath, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(alter);

            Assert.Equal(Describe(acePath, table), Describe(libredPath, table));

            byte[] ace = File.ReadAllBytes(acePath), libred = File.ReadAllBytes(libredPath);
            Assert.Equal(ace.Length, libred.Length);
            (int catalogPage, int usageMapPage, int lowestRecord) = Landmarks(acePath, table);
            for (int page = 1; page < ace.Length / 4096; page++)
            {
                var differ = Enumerable.Range(0, 4096).Where(i => ace[page * 4096 + i] != libred[page * 4096 + i]).ToList();
                if (differ.Count == 0 || BinaryPrimitives.ReadInt32LittleEndian(ace.AsSpan(page * 4096 + 4)) == catalogPage) continue;
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
        TableDef t = db.Catalog.FindTable(tableName)!;
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
        TableDef t = db.Catalog.FindTable(tableName)!;
        byte[] file = File.ReadAllBytes(path);
        int holder = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(t.DefinitionPage * 4096 + db.Format.TdefOwnedPagesOffset)) >> 8;
        int rows = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(holder * 4096 + db.Format.DataRowCountOffset));
        int lowest = Enumerable.Range(0, rows)
            .Select(i => BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(holder * 4096 + db.Format.DataRowDirectoryOffset + i * 2)))
            .Where(s => (s & 0x8000) == 0).Min(s => s & 0x1FFF);
        return (db.Catalog.FindTable("MSysObjects")!.DefinitionPage, holder, lowest);
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
