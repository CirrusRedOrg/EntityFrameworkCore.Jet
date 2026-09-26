using System.Buffers.Binary;
using System.Data.OleDb;
using System.Globalization;
using System.Reflection;
using System.Text;
using LibRed;
using LibRed.Catalog;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// About 140 statements run through ACE over OLE DB and through LibRed on copies of one ACE-created empty database,
/// and the files compared byte for byte. It reports how far apart they are rather than asserting they match.
/// </summary>
/// <remarks>
/// Two measurements. The whole run applies every statement in one session per engine and compares the two files at
/// the end — how close LibRed comes over a realistic workload. Step by step applies one statement at a time, both
/// engines starting from the same file — ACE's result of the step before — so each statement is compared on its own
/// and one early divergence does not leave every later page misaligned.
/// <para>Only what differs for reasons other than the format is masked: page 0's user commit-byte table, which
/// counts a lock-file user's writes and LibRed does not keep; the wall-clock DateCreate and DateUpdate of each
/// MSysObjects row; and a data page's write stamp at 0x08, with the copy of it a chained long value's descriptor
/// carries. A copy opened and closed through ACE with nothing
/// done to it shows what ACE writes merely for holding a session; a byte that alone changes is counted apart rather
/// than as a difference. Calculated and complex columns are left out — ACE's OLE DB DDL cannot create either.</para>
/// </remarks>
[Collection(AceCollection.Name)]
public class WholeFileParityProbeTest(ITestOutputHelper output)
{
    private const int PageSize = 4096;

    [Fact]
    public void The_whole_run_leaves_libred_and_ace_with_the_same_file()
    {
        string[] statements = Statements();
        string? origin = CreateEmptyThroughDao();
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        string ace = TemporaryDatabase.CopyPath(origin, "wholefile-ace-");
        string noise = TemporaryDatabase.CopyPath(origin, "wholefile-noise-");
        string libred = TemporaryDatabase.CopyPath(origin, "wholefile-libred-");
        try
        {
            string?[] aceErrors = RunThroughAce(ace, statements);
            using (AceTestDatabase.Open(noise)) { }
            string?[] libredErrors = RunThroughLibRed(libred, statements);

            var report = new StringBuilder();
            report.AppendLine(CultureInfo.InvariantCulture, $"{statements.Length} statements");
            for (int i = 0; i < statements.Length; i++)
                if (aceErrors[i] is not null || libredErrors[i] is not null)
                    report.AppendLine(CultureInfo.InvariantCulture,
                        $"  #{i + 1} {statements[i]}\n      ACE: {aceErrors[i] ?? "ok"}\n      LibRed: {libredErrors[i] ?? "ok"}");
            report.Append(Compare(origin, ace, noise, libred).Report);
            output.WriteLine(report.ToString());
        }
        finally
        {
            foreach (string path in new[] { origin, ace, noise, libred }) TemporaryDatabase.Delete(path);
        }
    }

    [Fact]
    public void Each_statement_leaves_libred_and_ace_with_the_same_file()
    {
        string[] statements = Statements();
        string? origin = CreateEmptyThroughDao();
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        string current = origin;
        var summary = new StringBuilder();
        var details = new StringBuilder();
        int same = 0;
        try
        {
            for (int i = 0; i < statements.Length; i++)
            {
                string ace = TemporaryDatabase.CopyPath(current, $"stepfile-ace{i}-");
                string noise = TemporaryDatabase.CopyPath(current, $"stepfile-noise{i}-");
                string libred = TemporaryDatabase.CopyPath(current, $"stepfile-libred{i}-");
                string? aceError = RunThroughAce(ace, [statements[i]])[0];
                using (AceTestDatabase.Open(noise)) { }
                string? libredError = RunThroughLibRed(libred, [statements[i]])[0];

                (string report, int pages) = Compare(current, ace, noise, libred);
                string outcome = aceError is not null || libredError is not null
                    ? $"ACE: {aceError ?? "ok"} | LibRed: {libredError ?? "ok"}"
                    : pages == 0 ? "identical" : $"{pages} page(s) differ";
                if (outcome == "identical") same++;
                summary.AppendLine(CultureInfo.InvariantCulture, $"#{i + 1,-3} {outcome,-22} {Abbreviate(statements[i])}");
                if (pages > 0)
                    details.AppendLine(CultureInfo.InvariantCulture, $"==== #{i + 1} {statements[i]}").Append(report);

                // The next statement starts from ACE's result, so each is measured on its own.
                if (current != origin) TemporaryDatabase.Delete(current);
                current = ace;
                TemporaryDatabase.Delete(noise);
                TemporaryDatabase.Delete(libred);
            }

            output.WriteLine($"{same} of {statements.Length} statements left identical files\n{summary}\n{details}");
        }
        finally
        {
            if (current != origin) TemporaryDatabase.Delete(current);
            TemporaryDatabase.Delete(origin);
        }
    }

    /// <summary>A hundred statements over six tables: creates with every ordinary column type and each kind of
    /// constraint, inserts, updates and deletes, more inserts into the space the deletes freed, a column added,
    /// retyped and dropped, indexes made and dropped, and a table dropped and another made after it. Then what
    /// spans pages: a thousand rows from one <c>INSERT … SELECT</c> over many data pages with a text index grown
    /// past one leaf, a 253-column table whose definition runs onto continuation pages, and long values chained
    /// across pages, grown, shrunk and deleted.</summary>
    private static string[] Statements()
    {
        var s = new List<string>
        {
            "CREATE TABLE Customer (Id COUNTER CONSTRAINT pkCustomer PRIMARY KEY, Name TEXT(50) NOT NULL, City TEXT(30), " +
                "Joined DATETIME, Credit CURRENCY, Active BIT)",
            "CREATE TABLE Product (Code TEXT(10) CONSTRAINT pkProduct PRIMARY KEY, Title TEXT(100), Price DOUBLE, " +
                "Weight REAL, Stock SHORT, Rating BYTE, Notes MEMO)",
            "CREATE TABLE Orders (OrderId LONG CONSTRAINT pkOrders PRIMARY KEY, CustomerId LONG, Placed DATETIME, " +
                "Total DECIMAL(18,4), Ref GUID, CONSTRAINT fkOrderCustomer FOREIGN KEY (CustomerId) REFERENCES Customer (Id))",
            "CREATE TABLE OrderLine (OrderId LONG, LineNo SHORT, ProductCode TEXT(10), Qty LONG, " +
                "CONSTRAINT pkOrderLine PRIMARY KEY (OrderId, LineNo))",
            "CREATE TABLE Scratch (Id LONG, Data VARBINARY(100), Flag BIT, Label TEXT(20))",
            "CREATE INDEX ixCustomerCity ON Customer (City)",
            "CREATE UNIQUE INDEX ixProductTitle ON Product (Title)",
            "CREATE INDEX ixOrderPlaced ON Orders (Placed DESC)",
        };

        string[] cities = ["London", "Paris", "Berlin", "Madrid", "Rome", "Oslo", "Vienna", "Prague", "Lisbon", "Dublin"];
        for (int i = 1; i <= 12; i++)
            s.Add($"INSERT INTO Customer (Name, City, Joined, Credit, Active) VALUES ('Customer {i:D2}', " +
                  $"'{cities[i % cities.Length]}', #2020-{(i % 12) + 1:D2}-{(i % 27) + 1:D2} {i % 24:D2}:15:00#, {i * 125.5m}, {(i % 3 == 0 ? "FALSE" : "TRUE")})");

        for (int i = 1; i <= 10; i++)
            s.Add($"INSERT INTO Product (Code, Title, Price, Weight, Stock, Rating, Notes) VALUES ('P{i:D3}', 'Product number {i}', " +
                  $"{i * 9.99}, {i * 0.25}, {i * 7}, {i * 20 % 256}, '{Notes(i)}')");

        for (int i = 1; i <= 12; i++)
            s.Add($"INSERT INTO Orders (OrderId, CustomerId, Placed, Total, Ref) VALUES ({1000 + i}, {(i % 9) + 1}, " +
                  $"#2021-{(i % 12) + 1:D2}-15#, {i * 101.1234m}, {{guid {{{new Guid(i, 0, 0, [1, 2, 3, 4, 5, 6, 7, (byte)i])}}}}})");

        for (int i = 1; i <= 16; i++)
            s.Add($"INSERT INTO OrderLine (OrderId, LineNo, ProductCode, Qty) VALUES ({1000 + (i % 8) + 1}, {i}, 'P{(i % 10) + 1:D3}', {i % 6})");

        for (int i = 1; i <= 6; i++)
            s.Add($"INSERT INTO Scratch (Id, Data, Flag, Label) VALUES ({i}, 0x{Convert.ToHexString([.. Enumerable.Range(i, 20).Select(b => (byte)b)])}, " +
                  $"{(i % 2 == 0 ? "TRUE" : "FALSE")}, 'scratch {i}')");

        s.AddRange(
        [
            "UPDATE Customer SET City = 'Paris' WHERE Id = 3",
            "UPDATE Customer SET Credit = Credit * 2 WHERE Active = TRUE",
            "UPDATE Product SET Price = Price + 1, Stock = Stock - 1",
            "UPDATE Product SET Notes = 'short note' WHERE Code = 'P002'",
            "UPDATE Orders SET Total = Total + 0.5 WHERE CustomerId = 2",
            "DELETE FROM OrderLine WHERE Qty < 2",
            "DELETE FROM Scratch WHERE Id > 4",
            "DELETE FROM Customer WHERE Id = 12",
            "DELETE FROM Product WHERE Code = 'P010'",
        ]);

        for (int i = 17; i <= 22; i++)
            s.Add($"INSERT INTO OrderLine (OrderId, LineNo, ProductCode, Qty) VALUES ({1000 + (i % 8) + 1}, {i}, 'P{(i % 9) + 1:D3}', {i})");

        s.AddRange(
        [
            "ALTER TABLE Customer ADD COLUMN Email TEXT(80)",
            "UPDATE Customer SET Email = 'c' & Id & '@example.com' WHERE Id < 6",
            "ALTER TABLE Product ALTER COLUMN Stock LONG",
            "ALTER TABLE Scratch DROP COLUMN Flag",
            "INSERT INTO Scratch (Id, Data, Label) VALUES (7, 0x0A0B0C, 'after drop')",
            "DROP INDEX ixCustomerCity ON Customer",
            "CREATE INDEX ixCustomerName ON Customer (Name)",
            "DROP TABLE Scratch",
            "CREATE TABLE Audit (Id COUNTER CONSTRAINT pkAudit PRIMARY KEY, Detail TEXT(255), LoggedOn DATETIME, Charge CURRENCY)",
        ]);

        for (int i = 1; i <= 8; i++)
            s.Add($"INSERT INTO Audit (Detail, LoggedOn, Charge) VALUES ('Audit entry {i} {new string('x', i * 20)}', #2022-03-{i:D2} 08:00:00#, {i * 3.25m})");

        s.AddRange(
        [
            "DELETE FROM Audit WHERE Id IN (2, 4, 6)",
            "INSERT INTO Audit (Detail, LoggedOn, Charge) VALUES ('refill one', #2022-04-01#, 1)",
            "INSERT INTO Audit (Detail, LoggedOn, Charge) VALUES ('refill two', #2022-04-02#, 2)",
            "ALTER TABLE Audit ADD COLUMN Severity BYTE",
            "UPDATE Audit SET Severity = Id MOD 4",
            "UPDATE Customer SET Name = 'Renamed ' & Name WHERE Id MOD 2 = 0",
            "DELETE FROM Orders WHERE OrderId = 1012",
            "INSERT INTO Customer (Name, City, Joined, Credit, Active, Email) VALUES ('Late comer', 'Oslo', #2023-01-01#, 10, TRUE, 'late@example.com')",
        ]);

        // A table over many data pages, filled by one INSERT … SELECT over a cross join, with an index that grows
        // past one leaf; deletes across its pages; and updates that grow rows until they have to move.
        s.Add("CREATE TABLE Digits (D BYTE)");
        for (int d = 0; d <= 9; d++) s.Add($"INSERT INTO Digits (D) VALUES ({d})");
        s.AddRange(
        [
            "CREATE TABLE Bulk (Id LONG CONSTRAINT pkBulk PRIMARY KEY, Grp LONG, Label TEXT(60), Payload TEXT(255), " +
                "Code BINARY(8), Amount DECIMAL(10,2))",
            "CREATE INDEX ixBulkLabel ON Bulk (Label)",
            "INSERT INTO Bulk (Id, Grp, Label, Payload, Amount) SELECT a.D * 100 + b.D * 10 + c.D, a.D, " +
                "'Bulk label number ' & (a.D * 100 + b.D * 10 + c.D) & ' ' & String(30, 'L'), String(150, 'p'), " +
                "a.D + b.D * 0.5 FROM Digits AS a, Digits AS b, Digits AS c ORDER BY 1",
            "DELETE FROM Bulk WHERE Id MOD 7 = 0",
            "UPDATE Bulk SET Payload = Payload & String(100, 'g') WHERE Id MOD 10 = 1",
            "UPDATE Bulk SET Label = 'R ' & Label WHERE Id < 50",
            "UPDATE Bulk SET Code = 0x0102030405060708 WHERE Id < 5",
            "INSERT INTO Bulk (Id, Grp, Label, Payload) VALUES (5000, 1, 'Late bulk', 'late')",
        ]);

        // A 253-column table, whose definition runs onto continuation pages, leaving room for one more column id
        // after a drop — ids are never reused before a compact, and 255 is the lifetime limit.
        string[] wideTypes = ["LONG", "TEXT(20)", "DOUBLE", "DATETIME", "CURRENCY", "BIT"];
        s.Add("CREATE TABLE Wide (Id LONG CONSTRAINT pkWide PRIMARY KEY, " +
              string.Join(", ", Enumerable.Range(1, 252).Select(i => $"Column{i:D3} {wideTypes[i % wideTypes.Length]}")) + ")");
        for (int i = 1; i <= 3; i++)
            s.Add($"INSERT INTO Wide (Id, Column001, Column002, Column252) VALUES ({i}, 'wide {i}', {i * 1.5}, {i})");
        s.AddRange(
        [
            "ALTER TABLE Wide DROP COLUMN Column005",
            "ALTER TABLE Wide ADD COLUMN Extra TEXT(10)",
            "UPDATE Wide SET Extra = 'extra' WHERE Id = 2",
        ]);

        // Long values across pages, the other binary types, and compressed text.
        s.AddRange(
        [
            "CREATE TABLE Doc (Id LONG CONSTRAINT pkDoc PRIMARY KEY, Body MEMO, Packed MEMO WITH COMPRESSION, " +
                "Brief TEXT(100) WITH COMPRESSION, Blob LONGBINARY, Big BIGBINARY(500))",
            $"INSERT INTO Doc VALUES (1, String(5000, 'b'), String(300, 'c'), 'short and compressed', 0x{Hex(6000, 1)}, 0x{Hex(400, 2)})",
            $"INSERT INTO Doc VALUES (2, String(2500, 'd'), 'packed two', 'two', 0x{Hex(100, 3)}, 0x{Hex(50, 4)})",
            "INSERT INTO Doc (Id, Body, Brief) VALUES (3, 'small', 'three')",
            "UPDATE Doc SET Body = String(8000, 'e') WHERE Id = 2",
            "UPDATE Doc SET Body = 'now small' WHERE Id = 1",
            $"UPDATE Doc SET Blob = 0x{Hex(9000, 5)} WHERE Id = 3",
            "DELETE FROM Doc WHERE Id = 2",
            "INSERT INTO Doc (Id, Body) VALUES (4, String(4000, 'f'))",
        ]);

        return [.. s];
    }

    /// <summary><paramref name="bytes"/> bytes as hex, a repeating pattern seeded by <paramref name="seed"/>.</summary>
    private static string Hex(int bytes, int seed) =>
        Convert.ToHexString([.. Enumerable.Range(0, bytes).Select(i => (byte)((i * 7 + seed) % 251))]);

    private static string Notes(int i) => string.Concat(Enumerable.Repeat($"Note {i}. ", i * 12));

    private static string Abbreviate(string statement) => statement.Length <= 90 ? statement : statement[..87] + "...";

    private static string?[] RunThroughAce(string path, string[] statements)
    {
        var errors = new string?[statements.Length];
        using OleDbConnection connection = AceTestDatabase.Open(path);
        for (int i = 0; i < statements.Length; i++)
        {
            try
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = statements[i];
                command.ExecuteNonQuery();
            }
            catch (OleDbException e) { errors[i] = e.Message; }
        }
        return errors;
    }

    private static string?[] RunThroughLibRed(string path, string[] statements)
    {
        var errors = new string?[statements.Length];
        using var database = JetDatabase.Open(path, readOnly: false);
        var engine = new QueryEngine(database);
        for (int i = 0; i < statements.Length; i++)
        {
            try { engine.ExecuteNonQuery(statements[i]); }
#pragma warning disable CA1031 // a probe records every refusal, whatever its type, and carries on
            catch (Exception e) { errors[i] = $"{e.GetType().Name}: {e.Message}"; }
#pragma warning restore CA1031
        }
        return errors;
    }

    /// <summary>Compares the two results of the same statements applied to <paramref name="originPath"/>: page
    /// counts, then every differing page — page 0 on its own, the rest grouped by page type and owner — with its
    /// differing byte count and the first few differences. Returns the report and how many pages differ.</summary>
    private static (string Report, int Pages) Compare(string originPath, string acePath, string noisePath, string libredPath)
    {
        byte[] origin = File.ReadAllBytes(originPath), ace = File.ReadAllBytes(acePath),
               noise = File.ReadAllBytes(noisePath), libred = File.ReadAllBytes(libredPath);
        Dictionary<int, string> owners = Owners(acePath, libredPath);
        HashSet<int>[] masks = [.. Enumerable.Range(0, Math.Max(ace.Length, libred.Length) / PageSize).Select(_ => new HashSet<int>())];
        Mask(acePath, ace, masks);
        Mask(libredPath, libred, masks);

        int acePages = ace.Length / PageSize, libredPages = libred.Length / PageSize;
        int identical = 0, housekeeping = 0, differingPages = 0;
        var groups = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        for (int page = 0; page < Math.Max(acePages, libredPages); page++)
        {
            int at = page * PageSize;
            if (page >= acePages || page >= libredPages)
            {
                byte[] only = page >= acePages ? libred : ace;
                Add(groups, "pages in one file only", $"page {page}: {(page >= acePages ? "LibRed" : "ACE")} only, " +
                    $"type 0x{only[at]:X2} {Owner(owners, only, at)}");
                differingPages++;
                continue;
            }

            var differing = new List<int>();
            for (int i = 0; i < PageSize; i++)
            {
                if (ace[at + i] == libred[at + i] || masks[page].Contains(i)) continue;
                // A byte ACE writes merely for holding a session, which LibRed need not reproduce.
                if (at + i < origin.Length && at + i < noise.Length && origin[at + i] != noise[at + i]) { housekeeping++; continue; }
                differing.Add(i);
            }
            if (differing.Count == 0) { identical++; continue; }

            differingPages++;
            string detail = string.Join(" ", differing.Take(8).Select(i => $"+0x{i:X3}:{ace[at + i]:X2}/{libred[at + i]:X2}"));
            string kind = page == 0 ? "page 0"
                : $"type 0x{ace[at]:X2}{(ace[at] != libred[at] ? $"/0x{libred[at]:X2}" : "")} {Owner(owners, ace, at)}";
            Add(groups, kind, $"page {page}: {differing.Count} bytes  {detail}");
        }

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"pages: before {origin.Length / PageSize}, ACE {acePages}, LibRed {libredPages}; identical {identical}; " +
            $"{housekeeping} byte(s) differ only where ACE's session alone writes (ace/libred shown)");
        foreach (var (kind, lines) in groups)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"-- {kind}: {lines.Count} page(s)");
            foreach (string line in lines) report.AppendLine(CultureInfo.InvariantCulture, $"   {line}");
        }
        return (report.ToString(), differingPages);
    }

    private static void Add(SortedDictionary<string, List<string>> groups, string kind, string line)
    {
        if (!groups.TryGetValue(kind, out var lines)) groups[kind] = lines = [];
        lines.Add(line);
    }

    /// <summary>What owns a page: for a data or index page the table whose TDEF page its header names, for a TDEF
    /// page its own table, where either file's catalog knows the name.</summary>
    private static string Owner(Dictionary<int, string> owners, byte[] file, int at)
    {
        if ((PageType)file[at] is PageType.TableDefinition or PageType.ReleasedTableDefinition)
            return owners.TryGetValue(at / PageSize, out string? self) ? $"({self})" : "";
        if ((PageType)file[at] is not (PageType.DataPage or PageType.IntermediateIndexPage or PageType.LeafIndexPage))
            return "";
        if (file.AsSpan(at + 4, 4).SequenceEqual("LVAL"u8)) return "(long values)";
        int owner = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 4, 4));
        return owners.TryGetValue(owner, out string? name) ? $"({name})" : $"(tdef {owner})";
    }

    private static Dictionary<int, string> Owners(params string[] paths)
    {
        var owners = new Dictionary<int, string>();
        foreach (string path in paths)
        {
            using var database = JetDatabase.Open(path, readOnly: true);
            foreach (TableDef table in database.Catalog.Tables) owners.TryAdd(table.DefinitionPage, table.Name);
        }
        return owners;
    }

    /// <summary>Masks the bytes known to differ for reasons other than the format: page 0's user commit-byte table,
    /// a count of each lock-file user's committed writes that LibRed, keeping no lock file, leaves alone
    /// (page-00-database.md §2.2); each MSysObjects row's wall-clock DateCreate and DateUpdate; and every data page's
    /// write stamp.</summary>
    private static void Mask(string path, byte[] file, HashSet<int>[] masks)
    {
        for (int i = 0xE00; i < PageSize; i++) masks[0].Add(i);

        using var database = JetDatabase.Open(path, readOnly: true);
        TableDef objects = database.Catalog.FindTable("MSysObjects")!;
        int[] dates = [.. new[] { "DateCreate", "DateUpdate" }.Select(c => objects.FindColumn(c)!.FixedOffset)];

        for (int page = 1; page < file.Length / PageSize; page++)
        {
            int at = page * PageSize;
            if ((PageType)file[at] != PageType.DataPage) continue;
            for (int i = 0x08; i < 0x0C; i++) masks[page].Add(i);
            if (BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 4, 4)) != objects.DefinitionPage) continue;

            int rows = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at + 0x0C, 2));
            for (int row = 0; row < rows; row++)
            {
                int slot = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at + 0x0E + 2 * row, 2));
                if ((slot & 0xC000) != 0) continue;
                int start = slot & 0x1FFF;
                foreach (int offset in dates)
                    for (int i = 0; i < 8; i++) masks[page].Add(start + 2 + offset + i);
            }
        }

        // A chained long value's descriptor carries the same write stamp as the chain's first page, bytes 8–11 of
        // its twelve (long-values.md).
        foreach (TableDef definition in database.Catalog.Tables)
        {
            if (!definition.Columns.Any(c => c.Type is JetDataType.Memo or JetDataType.Ole)) continue;
            Table table = database.OpenTable(definition.Name);
            foreach ((RowId id, _) in table.Rows().WithIds())
            {
                var page = new DataPage();
                page.Read(table.Channel.ReadPageShared(id.Page), table.Channel.Format);
                if (page.Rows[id.Row] is not { IsDeleted: false, HasOverflow: false } slot) continue;
                ReadOnlySpan<byte> row = page.GetRow(id.Row);
                foreach (byte[] descriptor in RowDecoder.LongValueDescriptors(definition.Columns, table.Channel.Format, row).Values)
                {
                    if (descriptor.Length < 12 || descriptor[3] != 0x00) continue;
                    int at = row.IndexOf(descriptor);
                    if (at >= 0)
                        for (int i = 8; i < 12; i++) masks[id.Page].Add(slot.Offset + at + i);
                }
            }
        }
    }

    /// <summary>An empty ACE 12 database made by DAO, the path Access itself takes; null where DAO is missing.</summary>
    private static string? CreateEmptyThroughDao()
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        if (engine is null) return null;
        string path = TemporaryDatabase.CreatePath("wholefile-origin-");
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
        object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
        Invoke(database, "Close");
        return path;
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
}
