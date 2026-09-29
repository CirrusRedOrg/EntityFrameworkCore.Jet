using System.Buffers.Binary;
using System.Data.OleDb;
using System.Globalization;
using LibRed;
using LibRed.Catalog;
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
            using (var database = JetDatabase.Open(libred, readOnly: false))
                new QueryEngine(database).ExecuteNonQuery(sql);

            output.WriteLine($"{insert}\nbefore {new FileInfo(origin).Length / 4096} pages\n" +
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
            using (var database = JetDatabase.Open(libred, readOnly: false))
            {
                var queries = new QueryEngine(database);
                foreach (string statement in sql) queries.ExecuteNonQuery(statement);
            }

            byte[] a = File.ReadAllBytes(ace), l = File.ReadAllBytes(libred);
            var report = new System.Text.StringBuilder($"{statements}\nACE {a.Length / 4096} pages, LibRed {l.Length / 4096} pages\n");
            for (int page = 1; page < Math.Min(a.Length, l.Length) / 4096; page++)
            {
                int at = page * 4096;
                if (!a.AsSpan(at + 4, 4).SequenceEqual("LVAL"u8)) continue;
                var ranges = new List<string>();
                for (int i = 0; i < 4096; i++)
                {
                    if (a[at + i] == l[at + i]) continue;
                    int start = i;
                    while (i + 1 < 4096 && (a[at + i + 1] != l[at + i + 1] || (i + 2 < 4096 && a[at + i + 2] != l[at + i + 2]))) i++;
                    ranges.Add($"0x{start:X3}-0x{i:X3} ACE {Convert.ToHexString(a, at + start, Math.Min(8, i - start + 1))}");
                }
                int rows = BinaryPrimitives.ReadUInt16LittleEndian(a.AsSpan(at + 12));
                string slots = string.Join(",", Enumerable.Range(0, rows)
                    .Select(r => $"0x{BinaryPrimitives.ReadUInt16LittleEndian(a.AsSpan(at + 14 + r * 2)) & 0x1FFF:X3}"));
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
        foreach ((int index, byte[] descriptor) in RowDecoder.LongValueDescriptors(doc.Definition.Columns, doc.Channel.Format, row)
                     .OrderBy(d => d.Key))
        {
            ColumnDef column = doc.Definition.Columns[index];
            byte flags = (byte)(descriptor[3] & 0xC0);
            int length = BinaryPrimitives.ReadInt32LittleEndian(descriptor) & 0x3FFFFFFF;
            string where = flags switch
            {
                0x80 => "inline",
                0x40 => $"single {Pointer(descriptor.AsSpan(4))}",
                _ => $"chained {string.Join(">", Chain(doc, descriptor))}",
            };
            parts.Add($"{column.Name}({length}) {where}");
        }
        return $"{new FileInfo(path).Length / 4096} pages, row on {id.Page}; " + string.Join("; ", parts);
    }

    private static List<string> Chain(Table table, byte[] descriptor)
    {
        var pages = new List<string>();
        int row = descriptor[4], pageNumber = descriptor[5] | (descriptor[6] << 8) | (descriptor[7] << 16);
        while (pageNumber != 0 && pages.Count < 64)
        {
            pages.Add($"{pageNumber}:{row}");
            var page = new DataPage();
            page.Read(table.Channel.ReadPageShared(pageNumber), table.Channel.Format);
            ReadOnlySpan<byte> record = page.GetRow(row);
            row = record[0];
            pageNumber = record[1] | (record[2] << 8) | (record[3] << 16);
        }
        return pages;
    }

    private static string Pointer(ReadOnlySpan<byte> at) => $"{at[1] | (at[2] << 8) | (at[3] << 16)}:{at[0]}";

    private static string Hex(int bytes) =>
        Convert.ToHexString([.. Enumerable.Range(0, bytes).Select(i => (byte)((i * 7 + 1) % 251))]);

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, System.Reflection.BindingFlags.InvokeMethod, null, target, args);
}
