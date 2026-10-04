using System.Data.OleDb;
using System.Globalization;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Probe: which pages ACE and LibRed give each long value of one inserted row, to see the order ACE writes a row's
/// long values in. Reports; asserts nothing.
/// </summary>
[Collection(AceCollection.Name)]
public class LongValueOrderProbeTest(ITestOutputHelper output)
{
    [Theory(Explicit = true)]
    [InlineData("INSERT INTO Doc VALUES (1, String(5000, 'b'), String(300, 'c'), 'short and compressed', 0x{6000}, 0x{400})")]
    [InlineData("INSERT INTO Doc (Id, Body, Blob) VALUES (1, String(5000, 'b'), 0x{6000})")]
    [InlineData("INSERT INTO Doc (Id, Body, Blob) VALUES (1, String(300, 'b'), 0x{6000})")]
    [InlineData("INSERT INTO Doc (Id, Body, Blob) VALUES (1, String(5000, 'b'), 0x{300})")]
    [InlineData("INSERT INTO Doc (Id, Body, Packed) VALUES (1, String(5000, 'b'), String(300, 'c'))")]
    [InlineData("INSERT INTO Doc (Id, Body, Blob) VALUES (1, String(5000, 'b'), 0x{12000})")]
    [InlineData("INSERT INTO Doc (Id, Body, Blob) VALUES (1, String(200, 'b'), 0x{600})")]
    [InlineData("INSERT INTO Doc (Id, Body, Blob) VALUES (1, String(1000, 'b'), 0x{600})")]
    public void Long_value_pages_of_one_row(string insert)
    {
        string sql = System.Text.RegularExpressions.Regex.Replace(insert, @"0x\{(\d+)\}",
            m => "0x" + Hex(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
        string origin = TemporaryDatabase.CreatePath("lvorder-origin-");
        string ace = TemporaryDatabase.CreatePath("lvorder-ace-");
        string libred = TemporaryDatabase.CreatePath("lvorder-libred-");
        try
        {
            object? engine = AceTestDatabase.CreateDaoEngine();
            Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
            object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
            object db = Invoke(workspace, "CreateDatabase", origin, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
            Invoke(db, "Close");
            using (OleDbConnection connection = AceTestDatabase.Open(origin))
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE Doc (Id LONG CONSTRAINT pkDoc PRIMARY KEY, Body MEMO, " +
                    "Packed MEMO WITH COMPRESSION, Brief TEXT(100) WITH COMPRESSION, Blob LONGBINARY, Big BIGBINARY(500))";
                command.ExecuteNonQuery();
            }
            File.Copy(origin, ace, overwrite: true);
            File.Copy(origin, libred, overwrite: true);

            using (OleDbConnection connection = AceTestDatabase.Open(ace))
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
            int pageSize;
            using (var database = JetDatabase.Open(libred, readOnly: false))
            {
                pageSize = database.Format.PageSize;
                new QueryEngine(database).ExecuteNonQuery(sql);
            }

            output.WriteLine($"{insert}\nbefore {new FileInfo(origin).Length / pageSize} pages\n" +
                             $"ACE:    {Describe(ace)}\nLibRed: {Describe(libred)}");
        }
        finally
        {
            foreach (string path in new[] { origin, ace, libred }) TemporaryDatabase.Delete(path);
        }
    }

    /// <summary>Compressed single-page memos: the bytes of every long-value page that differ between ACE and
    /// LibRed, as ranges with ACE's bytes at the start of each — to see what ACE leaves in a page's free space.</summary>
    [Theory(Explicit = true)]
    [InlineData("INSERT INTO Doc (Id, Packed) VALUES (1, String(300, 'c'))")]
    [InlineData("INSERT INTO Doc (Id, Packed) VALUES (1, String(300, 'c'))|INSERT INTO Doc (Id, Packed) VALUES (2, String(200, 'd'))")]
    [InlineData("INSERT INTO Doc (Id, Packed) VALUES (1, String(300, ChrW(20013)))")]
    [InlineData("INSERT INTO Doc (Id, Packed) VALUES (1, String(1800, 'e'))")]
    [InlineData("INSERT INTO Doc (Id, Body) VALUES (1, String(300, 'c'))")]
    [InlineData("INSERT INTO Doc (Id, Packed) VALUES (1, String(1800, 'e'))|INSERT INTO Doc (Id, Packed) VALUES (2, String(1000, 'f'))")]
    [InlineData("INSERT INTO Doc (Id, Packed) VALUES (1, String(1800, 'e'))|INSERT INTO Doc (Id, Packed) VALUES (2, String(1200, 'f'))")]
    public void Compressed_value_page_bytes(string statements)
    {
        string origin = TemporaryDatabase.CreatePath("lvbytes-origin-");
        string ace = TemporaryDatabase.CreatePath("lvbytes-ace-");
        string libred = TemporaryDatabase.CreatePath("lvbytes-libred-");
        try
        {
            object? engine = AceTestDatabase.CreateDaoEngine();
            Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
            object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
            object db = Invoke(workspace, "CreateDatabase", origin, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
            Invoke(db, "Close");
            using (OleDbConnection connection = AceTestDatabase.Open(origin))
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE Doc (Id LONG CONSTRAINT pkDoc PRIMARY KEY, Body MEMO, " +
                    "Packed MEMO WITH COMPRESSION, Brief TEXT(100) WITH COMPRESSION, Blob LONGBINARY, Big BIGBINARY(500))";
                command.ExecuteNonQuery();
            }
            File.Copy(origin, ace, overwrite: true);
            File.Copy(origin, libred, overwrite: true);

            string[] sql = statements.Split('|');
            using (OleDbConnection connection = AceTestDatabase.Open(ace))
                foreach (string statement in sql)
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = statement;
                    command.ExecuteNonQuery();
                }
            JetFormatBase format;
            using (var database = JetDatabase.Open(libred, readOnly: false))
            {
                format = database.Format;
                var queries = new QueryEngine(database);
                foreach (string statement in sql) queries.ExecuteNonQuery(statement);
            }

            int pageSize = format.PageSize;
            byte[] a = File.ReadAllBytes(ace), l = File.ReadAllBytes(libred);
            var report = new System.Text.StringBuilder($"{statements}\nACE {a.Length / pageSize} pages, LibRed {l.Length / pageSize} pages\n");
            for (int page = 1; page < Math.Min(a.Length, l.Length) / pageSize; page++)
            {
                int at = page * pageSize;
                if (DataPage.ReadOwner(a.AsSpan(at, pageSize), format) != JetFormatBase.LongValuePageMarker) continue;
                var ranges = new List<string>();
                for (int i = 0; i < pageSize; i++)
                {
                    if (a[at + i] == l[at + i]) continue;
                    int start = i;
                    while (i + 1 < pageSize && (a[at + i + 1] != l[at + i + 1] || (i + 2 < pageSize && a[at + i + 2] != l[at + i + 2]))) i++;
                    ranges.Add($"0x{start:X3}-0x{i:X3} ACE {Convert.ToHexString(a, at + start, Math.Min(8, i - start + 1))}");
                }
                int rows = DataPage.ReadRowCount(a.AsSpan(at, pageSize), format);
                string slots = string.Join(",", Enumerable.Range(0, rows)
                    .Select(r => $"0x{DataPage.ReadSlot(a.AsSpan(at, pageSize), format, r).Offset:X3}"));
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"  LVAL page {page}: ACE rows at [{slots}]; {(ranges.Count == 0 ? "identical" : string.Join("; ", ranges))}");
            }
            output.WriteLine(report.ToString());
        }
        finally
        {
            foreach (string path in new[] { origin, ace, libred }) TemporaryDatabase.Delete(path);
        }
    }

    /// <summary>Each long-value column of Doc's one row: its form (inline, single page, chained) and the pages
    /// its value occupies, first to last, with the file length.</summary>
    private static string Describe(string path)
    {
        using var database = JetDatabase.Open(path, readOnly: true);
        Table doc = database.OpenTable("Doc");
        (RowId id, _) = doc.Rows().WithIds().Single();
        var page = new DataPage();
        page.Read(doc.Channel.ReadPageShared(id.Page), doc.Channel.Format);
        byte[] row = page.GetRow(id.Row).ToArray();
        var parts = new List<string>();
        foreach ((int index, byte[] descriptor) in RowCodec.LongValueDescriptors(doc.Definition.Columns, doc.Channel.Format, row)
                     .OrderBy(d => d.Key))
        {
            ColumnDef column = doc.Definition.Columns[index];
            var value = LongValueStore.Read(descriptor, doc.Channel.Format);
            string where = value.Storage switch
            {
                LongValueStore.StorageKind.Inline => "inline",
                LongValueStore.StorageKind.SinglePage => $"single {value.Page}:{value.Row}",
                _ => $"chained {string.Join(">", Chain(doc, descriptor))}",
            };
            parts.Add($"{column.Name}({value.Length}) {where}");
        }
        return $"{new FileInfo(path).Length / database.Format.PageSize} pages, row on {id.Page}; " + string.Join("; ", parts);
    }

    private static List<string> Chain(Table table, byte[] descriptor)
    {
        var pages = new List<string>();
        var (_, _, row, pageNumber, _) = LongValueStore.Read(descriptor, table.Channel.Format);
        while (pageNumber != 0 && pages.Count < 64)
        {
            pages.Add($"{pageNumber}:{row}");
            var page = new DataPage();
            page.Read(table.Channel.ReadPageShared(pageNumber), table.Channel.Format);
            ReadOnlySpan<byte> record = page.GetRow(row);
            (row, pageNumber) = PageBuffer.ReadRecordPointer(record, 0);
        }
        return pages;
    }

    private static string Hex(int bytes) =>
        Convert.ToHexString([.. Enumerable.Range(0, bytes).Select(i => (byte)((i * 7 + 1) % 251))]);

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, System.Reflection.BindingFlags.InvokeMethod, null, target, args);
}