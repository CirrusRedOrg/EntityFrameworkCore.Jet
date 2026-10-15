using System.Buffers.Binary;
using System.Data.OleDb;
using System.Globalization;
using System.Reflection;
using System.Text;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A statement corpus run through ACE over OLE DB and through LibRed on copies of one ACE-created empty database,
/// and the files compared byte for byte.
/// </summary>
/// <remarks>
/// Two measurements. The whole run applies every statement in one session per engine and compares the two files at
/// the end — how close LibRed comes over a realistic workload; it reports, since one early placement difference
/// leaves every later page misaligned. Step by step applies one statement at a time, both engines starting from the
/// same file — ACE's result of the step before — so each statement is compared on its own, and it asserts that
/// every statement leaves identical files.
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
    [Theory]
    [InlineData(0)] // The entire current corpus; explicit prefixes locate continuous-session divergence.
    [InlineData(8, Explicit = true)]
    [InlineData(64, Explicit = true)]
    [InlineData(96, Explicit = true)]
    [InlineData(117, Explicit = true)]
    [InlineData(118, Explicit = true)]
    [InlineData(118, 430, Explicit = true)]
    [InlineData(118, 480, Explicit = true)]
    [InlineData(118, 600, Explicit = true)]
    [InlineData(118, 550, Explicit = true)]
    [InlineData(118, 590, Explicit = true)]
    [InlineData(118, 591, Explicit = true)]
    [InlineData(118, 820, Explicit = true)]
    [InlineData(118, 840, Explicit = true)]
    [InlineData(118, 880, Explicit = true)]
    [InlineData(118, 900, Explicit = true)]
    [InlineData(118, 910, Explicit = true)]
    [InlineData(118, 918, Explicit = true)]
    [InlineData(118, -900, Explicit = true)]
    [InlineData(118, 1000, 80, Explicit = true)]
    [InlineData(118, 1000, 120, Explicit = true)]
    [InlineData(118, 550, 120, Explicit = true)]
    [InlineData(118, 580, 120, Explicit = true)]
    [InlineData(118, 590, 120, Explicit = true)]
    [InlineData(118, 591, 120, Explicit = true)]
    [InlineData(118, 610, 120, Explicit = true)]
    [InlineData(118, 1000, 200, Explicit = true)]
    [InlineData(118, 920, Explicit = true)]
    [InlineData(119, Explicit = true)]
    [InlineData(120, Explicit = true)]
    [InlineData(124, Explicit = true)]
    [InlineData(128, Explicit = true)]
    [InlineData(132, Explicit = true)]
    [InlineData(136, Explicit = true)]
    [InlineData(140, Explicit = true)]
    [InlineData(150, Explicit = true)]
    [InlineData(151, Explicit = true)]
    [InlineData(155, Explicit = true)]
    [InlineData(162, Explicit = true)]
    [InlineData(164, Explicit = true)]
    [InlineData(168, Explicit = true)]
    [InlineData(176, Explicit = true)]
    [InlineData(184, Explicit = true)]
    [InlineData(192, Explicit = true)]
    [InlineData(200, Explicit = true)]
    [InlineData(247, Explicit = true)]
    [InlineData(253, Explicit = true)]
    [InlineData(265, Explicit = true)]
    [InlineData(270, Explicit = true)]
    [InlineData(300, Explicit = true)]
    public void The_whole_run_leaves_libred_and_ace_with_the_same_file(int count, int bulkRows = 1000, int payloadLength = 150)
    {
        string[] statements = Statements();
        if (count > 0) statements = statements[..count];
        if (payloadLength != 150)
            statements[117] = statements[117].Replace("String(150, 'p')", $"String({payloadLength}, 'p')", StringComparison.Ordinal);
        if (bulkRows < 0)
        {
            string bulk = statements[117];
            statements[117] = bulk.Replace("ORDER BY 1", $"WHERE a.D * 100 + b.D * 10 + c.D < {-bulkRows} ORDER BY 1", StringComparison.Ordinal);
            statements = [.. statements, bulk.Replace("ORDER BY 1", $"WHERE a.D * 100 + b.D * 10 + c.D >= {-bulkRows} ORDER BY 1", StringComparison.Ordinal)];
        }
        else if (bulkRows != 1000)
            statements[117] = statements[117].Replace("ORDER BY 1", $"WHERE a.D * 100 + b.D * 10 + c.D < {bulkRows} ORDER BY 1", StringComparison.Ordinal);
        string? origin = CreateEmptyThroughDao();
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        string ace = TemporaryDatabase.CopyPath(origin, "wholefile-ace-");
        string noise = TemporaryDatabase.CopyPath(origin, "wholefile-noise-");
        string libred = TemporaryDatabase.CopyPath(origin, "wholefile-libred-");
        try
        {
            void Snapshot(string path, string label)
            {
                if (bulkRows == 1000 || payloadLength != 150) return;
                Thread.Sleep(3000);
                string snapshot = TemporaryDatabase.CopyPath(path, "continuous-live-");
                try
                {
                    using var database = JetDatabase.Open(snapshot, readOnly: true);
                    var free = PageGroupProbeTest.GlobalFree(database.OpenTable("Bulk"), 256);
                    output.WriteLine($"{label} live free: {string.Join(",", free.Where(p => p >= 140 && p < 240).Order())}");
                }
                finally { TemporaryDatabase.Delete(snapshot); }
            }
            string?[] aceErrors = RunThroughAce(ace, statements, () => Snapshot(ace, "ACE"));
            using (AceTestDatabase.Open(noise)) { }
            string?[] libredErrors = RunThroughLibRed(libred, statements, () => Snapshot(libred, "LibRed"));

            var report = new StringBuilder();
            report.AppendLine(CultureInfo.InvariantCulture, $"{statements.Length} statements");
            for (int i = 0; i < statements.Length; i++)
                if (aceErrors[i] is not null || libredErrors[i] is not null)
                    report.AppendLine(CultureInfo.InvariantCulture,
                        $"  #{i + 1} {statements[i]}\n      ACE: {aceErrors[i] ?? "ok"}\n      LibRed: {libredErrors[i] ?? "ok"}");
            report.Append(Compare(origin, ace, noise, libred).Report);
            if (statements.Length >= 117)
            {
                report.AppendLine($"ACE placement: {PageGroupProbeTest.Runs(ace)}");
                report.AppendLine($"LibRed placement: {PageGroupProbeTest.Runs(libred)}");
                foreach (string path in new[] { ace, libred })
                {
                    using var database = JetDatabase.Open(path, readOnly: true);
                    Table bulk = database.OpenTable("Bulk");
                    foreach (var index in bulk.Definition.Indexes)
                        report.AppendLine($"{(path == ace ? "ACE" : "LibRed")} {index.Name} pages: {string.Join(",", bulk.UsageMap.PagesInMap(index.UsageMap.Row, index.UsageMap.Page))}");
                }
            }
            output.WriteLine(report.ToString());
        }
        finally
        {
            foreach (string path in new[] { origin, ace, noise, libred }) TemporaryDatabase.Delete(path);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(138)]
    [InlineData(-1)] // The bulk-allocation statement, independent of corpus numbering.
    public void Each_statement_leaves_libred_and_ace_with_the_same_file(int statement)
    {
        string[] statements = Statements();
        string? origin = CreateEmptyThroughDao();
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        string current = origin;
        var summary = new StringBuilder();
        var details = new StringBuilder();
        var outcomes = new string[statements.Length];
        int same = 0;
        try
        {
            for (int i = 0; i < statements.Length; i++)
            {
                string ace = TemporaryDatabase.CopyPath(current, $"stepfile-ace{i}-");
                bool selected = statement == 0 || statement > 0 && i + 1 == statement
                    || statement == -1 && statements[i].StartsWith("INSERT INTO Bulk (Id, Grp, Label, Payload, Amount) SELECT", StringComparison.Ordinal);
                if (!selected)
                {
                    Assert.Null(RunThroughAce(ace, [statements[i]])[0]);
                    if (current != origin) TemporaryDatabase.Delete(current);
                    current = ace;
                    continue;
                }
                string noise = TemporaryDatabase.CopyPath(current, $"stepfile-noise{i}-");
                string libred = TemporaryDatabase.CopyPath(current, $"stepfile-libred{i}-");
                string? aceError = RunThroughAce(ace, [statements[i]])[0];
                using (AceTestDatabase.Open(noise)) { }
                string? libredError = RunThroughLibRed(libred, [statements[i]])[0];
                CopyQueryPlans(ace, libred);

                (string report, int pages) = Compare(current, ace, noise, libred);
                if (statement == -1)
                {
                    output.WriteLine($"ACE placement: {PageGroupProbeTest.Runs(ace)}");
                    output.WriteLine($"LibRed placement: {PageGroupProbeTest.Runs(libred)}");
                }
                string outcome = aceError is not null || libredError is not null
                    ? $"ACE: {aceError ?? "ok"} | LibRed: {libredError ?? "ok"}"
                    : pages == 0 ? "identical" : $"{pages} page(s) differ";
                if (outcome == "identical") same++;
                outcomes[i] = outcome;
                summary.AppendLine(CultureInfo.InvariantCulture, $"#{i + 1,-3} {outcome,-22} {Abbreviate(statements[i])}");
                if (pages > 0)
                    details.AppendLine(CultureInfo.InvariantCulture, $"==== #{i + 1} {statements[i]}").Append(report);

                // The next statement starts from ACE's result, so each is measured on its own.
                if (current != origin) TemporaryDatabase.Delete(current);
                current = ace;
                TemporaryDatabase.Delete(noise);
                TemporaryDatabase.Delete(libred);
                if (statement != 0) break;
            }

            output.WriteLine($"{same} of {statements.Length} statements left identical files\n{summary}\n{details}");

            var unexpected = new List<string>();
            for (int i = 0; i < statements.Length; i++)
            {
                if (outcomes[i] is null) continue;
                if (outcomes[i] != "identical")
                    unexpected.Add($"#{i + 1} {outcomes[i]}: {Abbreviate(statements[i])}");
            }
            Assert.True(unexpected.Count == 0, string.Join("\n", unexpected));
        }
        finally
        {
            if (current != origin) TemporaryDatabase.Delete(current);
            TemporaryDatabase.Delete(origin);
        }
    }

    /// <summary>Statements over ordinary column types and each kind of
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

        // Stored queries: views and procedures of each shape, run and dropped.
        s.AddRange(
        [
            "CREATE VIEW vActiveCustomer AS SELECT Name, City FROM Customer WHERE Active = TRUE",
            "CREATE VIEW vOrderTotal AS SELECT c.Name, o.Total FROM Customer AS c INNER JOIN Orders AS o ON c.Id = o.CustomerId",
            "CREATE VIEW vProductLine AS SELECT p.Code, l.Qty FROM Product AS p LEFT JOIN OrderLine AS l ON p.Code = l.ProductCode",
            "CREATE VIEW vCityCount AS SELECT City, COUNT(*) AS Customers FROM Customer GROUP BY City HAVING COUNT(*) > 0",
            "CREATE VIEW vCity AS SELECT DISTINCT City FROM Customer",
            "CREATE VIEW vCheapest AS SELECT TOP 3 Code, Price FROM Product",
            "CREATE VIEW vPriced AS SELECT Code, Price * 2 AS Doubled, Title & '!' AS Shout FROM Product WHERE Price BETWEEN 10 AND 90",
            "CREATE PROCEDURE pByCity (cityName TEXT(30)) AS SELECT Id, Name FROM Customer WHERE City = cityName",
            "CREATE PROCEDURE pSorted AS SELECT Name FROM Customer ORDER BY Name DESC",
            "CREATE PROCEDURE pBump (amount CURRENCY) AS UPDATE Customer SET Credit = Credit + amount WHERE Active = TRUE",
            "CREATE PROCEDURE pPurge AS DELETE FROM Audit WHERE Severity = 0",
            "CREATE PROCEDURE pLog AS INSERT INTO Audit (Detail, LoggedOn, Charge) VALUES ('from a procedure', #2022-05-01#, 1)",
            "CREATE PROCEDURE pCopy AS INSERT INTO Audit (Detail, LoggedOn, Charge) SELECT Name, Joined, Credit FROM Customer WHERE Id < 3",
            "EXECUTE pLog",
            "EXECUTE pPurge",
            "EXECUTE pCopy",
            "DROP VIEW vCheapest",
            "DROP PROCEDURE pPurge",
            "CREATE VIEW vDerived AS SELECT d.City, d.N FROM (SELECT City, COUNT(*) AS N FROM Customer GROUP BY City) AS d WHERE d.N > 0",
            "CREATE PROCEDURE pTyped (since DATETIME, lim LONG) AS SELECT Id FROM Customer WHERE Joined > since AND Id < lim",
            "CREATE PROCEDURE pSized (lim LONG, who TEXT(20)) AS SELECT Id FROM Customer WHERE Id < lim AND Name <> who ORDER BY Id",
            "CREATE PROCEDURE pSizedFirst (who TEXT(20), lim LONG) AS SELECT Id FROM Customer WHERE Id < lim AND Name <> who",
            "CREATE PROCEDURE pRename (who TEXT(20)) AS UPDATE Customer SET Name = who WHERE Id = 999",
            "CREATE PROCEDURE pCopyJoin AS INSERT INTO Audit (Detail, Charge) SELECT c.Name, o.Total FROM Customer AS c " +
                "INNER JOIN Orders AS o ON c.Id = o.CustomerId WHERE o.Total > 0",
            "CREATE PROCEDURE pMake AS SELECT Name, City INTO CustomerNames FROM Customer WHERE Id < 5",
            "CREATE VIEW vDistinctWhere AS SELECT DISTINCT City FROM Customer WHERE Id > 0",
            "CREATE PROCEDURE pMixed AS SELECT DISTINCT TOP 2 c.City FROM Customer AS c INNER JOIN Orders AS o ON c.Id = o.CustomerId " +
                "WHERE o.Total > 0 GROUP BY c.City HAVING COUNT(*) > 0 ORDER BY c.City",
        ]);

        // Reserved words as names, bracketed; defaults; CHECK constraints; a column-level REFERENCES dropped for a
        // cascading one, and the cascades it then makes.
        s.AddRange(
        [
            "CREATE TABLE Setting (Id LONG CONSTRAINT pkSetting PRIMARY KEY, Name TEXT(40) DEFAULT 'unnamed', " +
                "Enabled BIT DEFAULT TRUE, [Level] LONG DEFAULT 3, Created DATETIME DEFAULT Now(), [Note] TEXT(20), " +
                "CONSTRAINT uqSettingName UNIQUE (Name))",
            "INSERT INTO Setting (Id, Created) VALUES (1, #2024-01-01#)",
            "INSERT INTO Setting (Id, Name, Created) VALUES (2, 'second', #2024-01-02#)",
            "INSERT INTO Setting (Id, Name, [Level], Created, [Note]) VALUES (3, 'third', 9, #2024-01-03#, 'a note')",
            "INSERT INTO Setting (Id, Name, Enabled, Created) VALUES (4, 'a name longer than the default', FALSE, #2024-01-04#)",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingLevel CHECK ([Level] BETWEEN 0 AND 10)",
            "UPDATE Setting SET [Level] = 7 WHERE Id = 2",
            "UPDATE Setting SET Name = 'x' WHERE Id = 3",
            "UPDATE Setting SET [Note] = 'a longer note!' WHERE Id = 3",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingLevel",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingPair CHECK ([Level] < Len([Note]) + 20)",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingOther CHECK ([Level] <= (SELECT COUNT(*) FROM Customer) + 10)",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingNone CHECK (1 = 1)",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingPair",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingNone",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingOther",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingOrder CHECK (Len([Note]) < [Level] + 20 OR [Level] > 0)",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingCredit CHECK ([Level] < (SELECT MAX(Credit) FROM Customer WHERE City = 'Rome') + 10)",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingOrder",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingCredit",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingFirst CHECK ((SELECT COUNT(*) FROM Audit) >= [Level] - 100)",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingFirst",
            "ALTER TABLE Setting ADD CONSTRAINT ckShared CHECK ([Level] >= 0)",
            "ALTER TABLE Setting DROP CONSTRAINT ckShared",
            "ALTER TABLE Setting ADD CONSTRAINT Customer CHECK ([Level] >= 0)",
            "ALTER TABLE Setting DROP CONSTRAINT Customer",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingOwn CHECK (Setting.[Level] >= 0 and Setting.[Level]  <  100)",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingRow CHECK ((SELECT COUNT(*) FROM Customer WHERE Customer.Name = Setting.Name) >= 0)",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingOwn",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingRow",
            "CREATE TABLE Ranged (Id LONG, Lo LONG, Hi LONG, CONSTRAINT ckRange CHECK (Lo <= Hi))",
            "INSERT INTO Ranged VALUES (1, 2, 3)",
            "DROP TABLE Ranged",
            "CREATE TABLE Child (Id LONG CONSTRAINT pkChild PRIMARY KEY, SettingId LONG CONSTRAINT fkChildSetting REFERENCES Setting (Id), " +
                "Label TEXT(20))",
            "INSERT INTO Child VALUES (1, 1, 'one')",
            "INSERT INTO Child VALUES (2, 2, 'two')",
            "INSERT INTO Child VALUES (3, 3, 'three')",
            "ALTER TABLE Child DROP CONSTRAINT fkChildSetting",
            "ALTER TABLE Child ADD CONSTRAINT fkChildCascade FOREIGN KEY (SettingId) REFERENCES Setting (Id) ON DELETE CASCADE ON UPDATE CASCADE",
            "DELETE FROM Setting WHERE Id = 2",
            "UPDATE Setting SET Id = 10 WHERE Id = 1",
            "ALTER TABLE Child ADD COLUMN Weight DOUBLE",
            "UPDATE Child SET Weight = Id * 1.5",
            "ALTER TABLE Child ALTER COLUMN Label TEXT(50)",
            "ALTER TABLE Child ALTER COLUMN Weight LONG",
            "CREATE INDEX ixChildLabel ON Child (Label) WITH IGNORE NULL",
            "CREATE UNIQUE INDEX ixChildPair ON Child (SettingId, Label) WITH DISALLOW NULL",
            "DROP INDEX ixChildLabel ON Child",
            "CREATE TABLE Slack (K LONG, A TEXT(20) DEFAULT 'aaaa', B TEXT(20) DEFAULT 'bbbbbbbb', C TEXT(20))",
            "INSERT INTO Slack (K, A, B, C) VALUES (1, 'x', 'y', 'z')",
            "INSERT INTO Slack (K, C, B, A) VALUES (2, 'z', 'y', 'x')",
            "INSERT INTO Slack (K, C) VALUES (3, 'zzzzzzzzzzzzzzzz')",
            "INSERT INTO Slack (K, B) VALUES (4, 'z')",
            "INSERT INTO Slack (K, A, B) VALUES (5, NULL, '')",
            "UPDATE Slack SET A = 'xx', C = 'zzzzzz' WHERE K = 1",
            "UPDATE Slack SET C = 'zzzzzz', A = '' WHERE K = 2",
            "UPDATE Slack SET A = 'xxxxxxxxxxxxxxxxxxxx' WHERE K = 4",
            "UPDATE Slack SET K = 50 WHERE K = 5",
            "CREATE TABLE Prop1 (Id LONG, Name TEXT(40) DEFAULT 'unnamed', Enabled BIT DEFAULT TRUE, Tier LONG DEFAULT 3, " +
                "CONSTRAINT uqProp1 UNIQUE (Name))",
            "CREATE TABLE Prop2 (Id LONG, Name TEXT(40) DEFAULT 'unnamed', Enabled BIT DEFAULT TRUE, Tier LONG DEFAULT 3, " +
                "CONSTRAINT pkProp2 PRIMARY KEY (Id))",
            "CREATE TABLE Prop3 (Id LONG, Name TEXT(40) DEFAULT 'unnamed' CONSTRAINT uqProp3 UNIQUE, Enabled BIT DEFAULT TRUE, " +
                "Tier LONG DEFAULT 3)",
            "CREATE TABLE Prop4 (Id LONG, A TEXT(40) DEFAULT 'unnamed' NOT NULL, B LONG DEFAULT 3 NOT NULL, C TEXT(10) NOT NULL, " +
                "D LONG DEFAULT 4)",
            "CREATE TABLE Prop5 (Id LONG NOT NULL, A TEXT(40) NOT NULL, B LONG NOT NULL, C TEXT(10) NOT NULL, D LONG NOT NULL, " +
                "E LONG NOT NULL)",
            "CREATE TABLE Prop6 (Id LONG CONSTRAINT pkProp6 PRIMARY KEY, A TEXT(10) NOT NULL CONSTRAINT uqProp6 UNIQUE, " +
                "B LONG DEFAULT 1, C TEXT(20) DEFAULT 'a longer default value')",
            "CREATE TABLE [Order] ([Select] LONG CONSTRAINT pkOrder PRIMARY KEY, [From] TEXT(10), [Where] DATETIME, [Money] CURRENCY)",
            "INSERT INTO [Order] VALUES (1, 'here', #2025-02-03#, 12.5)",
            "UPDATE [Order] SET [From] = 'there', [Money] = [Money] * 3 WHERE [Select] = 1",
        ]);

        // Make-table queries, a table emptied and refilled, a primary key replaced, and more retypes.
        s.AddRange(
        [
            "SELECT * INTO CustomerCopy FROM Customer",
            "SELECT Code, Price INTO Cheap FROM Product WHERE Price < 50",
            "SELECT City, COUNT(*) AS N, SUM(Credit) AS Total INTO CityTotals FROM Customer GROUP BY City",
            "DROP TABLE Cheap",
            "DELETE FROM CustomerCopy WHERE Id > 0",
            "INSERT INTO CustomerCopy (Name, City, Joined, Credit, Active) SELECT Name, City, Joined, Credit, Active FROM Customer WHERE Id < 4",
            "ALTER TABLE OrderLine DROP CONSTRAINT pkOrderLine",
            "ALTER TABLE OrderLine ADD CONSTRAINT pkOrderLineAgain PRIMARY KEY (OrderId, LineNo)",
            "CREATE INDEX ixAuditSeverity ON Audit (Severity, LoggedOn DESC)",
            "CREATE UNIQUE INDEX ixOrdersRef ON Orders (Ref)",
            "ALTER TABLE Audit ALTER COLUMN Charge DOUBLE",
            "ALTER TABLE Audit ALTER COLUMN Severity TEXT(5)",
            "ALTER TABLE Audit ALTER COLUMN Detail MEMO",
            "ALTER TABLE Audit ALTER COLUMN LoggedOn DOUBLE",
            "UPDATE Customer SET Joined = DateAdd('m', 1, Joined) WHERE Joined IS NOT NULL",
            "UPDATE Product SET Title = Title & ' (' & Code & ')'",
            "UPDATE Customer SET City = UCase(City), Email = NULL WHERE Id IN (SELECT CustomerId FROM Orders WHERE Total > 500)",
            "DELETE FROM OrderLine WHERE ProductCode NOT IN (SELECT Code FROM Product)",
        ]);

        // Every ordinary type in one table, an AutoNumber with its own seed and step, and compressed text.
        s.AddRange(
        [
            "CREATE TABLE Kinds (Id LONG, Small SHORT, Tiny BYTE, Real4 REAL, Cash CURRENCY, [Exact] DECIMAL(28,10), Flag BIT, " +
                "Moment DATETIME, Ident GUID, Raw BINARY(16), Var VARBINARY(50), Fixed CHAR(5), Packed TEXT(40) WITH COMPRESSION)",
            "INSERT INTO Kinds VALUES (1, -32768, 255, 1.5, 123.4567, -123456789.0123456789, TRUE, #1899-12-30#, " +
                "{guid {00000001-0002-0003-0405-060708090A0B}}, 0x00FF00FF00FF00FF00FF00FF00FF00FF, 0x0102, 'ab', 'plain latin text')",
            "INSERT INTO Kinds (Id) VALUES (2)",
            "INSERT INTO Kinds (Id, Small, Real4, Cash, [Exact], Moment, Packed) VALUES (3, 32767, -3.4E38, -0.0001, 0.0000000001, " +
                "#9999-12-31 23:59:59#, 'ünïcödé — not latin')",
            "UPDATE Kinds SET [Exact] = [Exact] * -1, Cash = Cash * 2 WHERE Id <> 1",
            "UPDATE Kinds SET Fixed = 'abcde', Var = 0x0A0B0C0D, Packed = 'now packed' WHERE Id = 2",
            "CREATE TABLE Seq (Id AUTOINCREMENT(10, 5) CONSTRAINT pkSeq PRIMARY KEY, Label TEXT(10))",
            "INSERT INTO Seq (Label) VALUES ('a')",
            "INSERT INTO Seq (Label) VALUES ('b')",
            "INSERT INTO Seq (Label) VALUES ('c')",
            "DELETE FROM Seq WHERE Id = 15",
            "INSERT INTO Seq (Label) VALUES ('d')",
            "SELECT * INTO SeqCopy FROM Seq",
            "SELECT OrderId, CustomerId * 2 AS Doubled, Total * 1.5 AS Scaled, Placed + 1 AS NextDay, Total > 100 AS Big, " +
                "CustomerId & '' AS AsText, Ref AS SameRef INTO OrderCalc FROM Orders",
            "SELECT CByte(Rating) AS B1, CInt(Stock) AS I2, CLng(Stock) AS L4, CSng(Price) AS S4, CDbl(Price) AS D8, " +
                "CCur(Price) AS C8, CDate(Price) AS T8, CBool(Stock) AS Y1, Price > 10 AS Cmp, Price AS Raw, Code & '' AS Txt " +
                "INTO Casts FROM Product",
            "SELECT Total * 2 AS Dec4, Total / 3 AS DecDiv, CCur(Total) * 2 AS Cur2 INTO MoneyCalc FROM Orders",
            "CREATE TABLE Dz (A DECIMAL(10,2), M CURRENCY)",
            "INSERT INTO Dz VALUES (12.34, 5.5)",
            "SELECT A * 2 AS A2, A / 3 AS A3, A * 1.5 AS A4, M * 2 AS M2, SUM(M) AS M3 INTO DzCalc FROM Dz GROUP BY A, M",
        ]);

        // Every row of a table deleted: with no WHERE, through a WHERE that every row meets, over many pages and
        // two indexes, and over a keyed AutoNumber table; then a table dropped that other objects named.
        s.AddRange(
        [
            "DELETE FROM Digits WHERE D >= 0",
            "DELETE FROM Bulk",
            "DELETE FROM Seq",
            "INSERT INTO Seq (Label) VALUES ('after')",
            "DROP TABLE Child",
            "DROP TABLE Setting",
        ]);

        // A whole table deleted with no WHERE, over each shape: long values chained and packed, no index at all,
        // the DELETE * form, a definition on continuation pages, a table already empty, a composite key, the child
        // and then the parent of a relationship, a cascading parent, and a WHERE that is always true.
        s.AddRange(
        [
            "DELETE FROM Doc",
            "INSERT INTO Doc (Id, Body) VALUES (5, String(3000, 'g'))",
            "DELETE FROM Slack",
            "DELETE * FROM Kinds",
            "DELETE FROM Wide",
            "DELETE FROM Digits",
            "DELETE FROM OrderLine",
            "DELETE FROM Orders",
            "DELETE FROM Customer",
            "INSERT INTO Customer (Name, City) VALUES ('Fresh', 'Rome')",
            "CREATE TABLE Parent (Id LONG CONSTRAINT pkParent PRIMARY KEY, Name TEXT(20))",
            "CREATE TABLE Kid (Id LONG CONSTRAINT pkKid PRIMARY KEY, ParentId LONG, " +
                "CONSTRAINT fkKidParent FOREIGN KEY (ParentId) REFERENCES Parent (Id) ON DELETE CASCADE)",
            "INSERT INTO Parent VALUES (1, 'one')",
            "INSERT INTO Parent VALUES (2, 'two')",
            "INSERT INTO Kid VALUES (10, 1)",
            "INSERT INTO Kid VALUES (20, 2)",
            "INSERT INTO Kid VALUES (21, 2)",
            "DELETE FROM Parent",
            "CREATE TABLE Checked (Id LONG CONSTRAINT pkChecked PRIMARY KEY, Amount LONG, Body MEMO, " +
                "CONSTRAINT ckChecked CHECK (Amount >= 0))",
            "INSERT INTO Checked VALUES (1, 10, String(3000, 'a'))",
            "INSERT INTO Checked VALUES (2, 20, 'short')",
            "INSERT INTO Checked VALUES (3, 30, String(200, 'c'))",
            "DELETE FROM Checked",
            "DELETE FROM SeqCopy WHERE 1 = 1",
            "DELETE FROM CityTotals",
            "DELETE FROM Audit",
        ]);

        // Tables refilled through INSERT … SELECT, then multi-table UPDATE and DELETE through joins; constraints and
        // indexes added and dropped over rows; long values grown and nulled; columns retyped, dropped and added
        // with a default; make-tables with DISTINCT and TOP; reserved names; a fresh AutoNumber memo table; two UNION
    // procedures; and indexes sharing one data block with another index or a relationship, then dropped.
        s.AddRange(
        [
            "INSERT INTO Orders (OrderId, CustomerId, Placed, Total) SELECT 2000 + Id, Id, Joined, Credit FROM Customer",
            "INSERT INTO Customer (Name, City, Credit) VALUES ('Second', 'Oslo', 20)",
            "INSERT INTO Orders (OrderId, CustomerId, Total) SELECT 3000 + Id, Id, 5 FROM Customer",
            "INSERT INTO OrderLine (OrderId, LineNo, ProductCode, Qty) SELECT o.OrderId, p.Stock, p.Code, 3 FROM Orders AS o, Product AS p WHERE p.Price < 40",
            "UPDATE Orders AS o INNER JOIN Customer AS c ON o.CustomerId = c.Id SET o.Total = o.Total + 1, c.City = 'Turin'",
            "DELETE o.* FROM OrderLine AS o INNER JOIN Product AS p ON o.ProductCode = p.Code WHERE p.Price < 20",
            "ALTER TABLE Product ADD COLUMN Discontinued BIT",
            "UPDATE Product SET Discontinued = TRUE WHERE Stock < 30",
            "CREATE UNIQUE INDEX ixProductCodeTitle ON Product (Code, Title DESC)",
            "ALTER TABLE Product ADD CONSTRAINT uqProductWeight UNIQUE (Weight)",
            "ALTER TABLE Product DROP CONSTRAINT uqProductWeight",
            "UPDATE Product SET Notes = String(3000, 'n') WHERE Code = 'P003'",
            "UPDATE Product SET Notes = NULL WHERE Code = 'P004'",
            "INSERT INTO Kinds (Id, Packed) VALUES (10, 'ünïcödé — 中文')",
            "UPDATE Kinds SET Packed = Packed & ' more' WHERE Id = 10",
            "ALTER TABLE Kinds ALTER COLUMN Small LONG",
            "ALTER TABLE Kinds DROP COLUMN Raw",
            "CREATE INDEX ixKindsMoment ON Kinds (Moment DESC, Id)",
            "INSERT INTO Kinds (Id, Moment) VALUES (11, #2001-02-03 04:05:06#)",
            "DROP INDEX ixKindsMoment ON Kinds",
            "ALTER TABLE Seq ADD COLUMN Extra LONG DEFAULT 7",
            "INSERT INTO Seq (Label) VALUES ('def')",
            "SELECT DISTINCT City INTO CityList FROM Customer",
            "SELECT TOP 2 * INTO TopProducts FROM Product ORDER BY Price DESC",
            "INSERT INTO [Order] ([Select], [From]) VALUES (2, 'there')",
            "UPDATE [Order] SET [Where] = #2030-01-01# WHERE [Select] = 2",
            "DELETE FROM [Order] WHERE [Select] = 1",
            "DROP TABLE [Order]",
            "CREATE TABLE Notes2 (Id COUNTER CONSTRAINT pkNotes2 PRIMARY KEY, Body LONGTEXT, Tag TEXT(10) DEFAULT 'none' NOT NULL)",
            "INSERT INTO Notes2 (Body) VALUES ('first')",
            "INSERT INTO Notes2 (Body, Tag) VALUES (String(200, 'x'), 'long')",
            "UPDATE Notes2 SET Body = Body & Body WHERE Id = 2",
            "DELETE FROM Notes2 WHERE Id = 1",
            "INSERT INTO Notes2 (Body) SELECT Name FROM Customer",
            "CREATE PROCEDURE pUnion (who TEXT(20)) AS SELECT Name FROM Customer WHERE Name <> who " +
                "UNION SELECT Code FROM Product ORDER BY Name DESC",
            "CREATE PROCEDURE pUnionAll AS TABLE Seq UNION ALL SELECT Id, Label, Extra FROM Seq WITH OWNERACCESS OPTION",
            "CREATE INDEX ixSeqLabelA ON Seq (Label)",
            "CREATE INDEX ixSeqLabelB ON Seq (Label)",
            "DROP INDEX ixSeqLabelA ON Seq",
            "CREATE INDEX ixOrdersCustomer ON Orders (CustomerId)",
            "DROP INDEX ixOrdersCustomer ON Orders",
            "CREATE INDEX ixOrdersCustomer2 ON Orders (CustomerId)",
            "ALTER TABLE Orders DROP CONSTRAINT fkOrderCustomer",
            "CREATE TABLE ShP (Id LONG CONSTRAINT pkShP PRIMARY KEY)",
            "CREATE TABLE ShC (Id LONG, P LONG)",
            "CREATE INDEX ixShCP ON ShC (P)",
            "ALTER TABLE ShC ADD CONSTRAINT fkShC FOREIGN KEY (P) REFERENCES ShP (Id)",
            "DROP TABLE ShC",
            "CREATE TABLE ShU (K LONG, C TEXT(10), CONSTRAINT u1 UNIQUE (C), CONSTRAINT u2 UNIQUE (C))",
            "CREATE TABLE ShU2 (C TEXT(10) CONSTRAINT u3 UNIQUE, M MEMO, CONSTRAINT u4 UNIQUE (C))",
            "CREATE TABLE ShF (K LONG, P LONG, CONSTRAINT r1 FOREIGN KEY (P) REFERENCES ShP (Id), " +
                "CONSTRAINT r2 FOREIGN KEY (P) REFERENCES Seq (Id))",
            "INSERT INTO ShU (K, C) VALUES (1, 'a')",
            "DROP INDEX u1 ON ShU",
        ]);

        return [.. s];
    }

    /// <summary><paramref name="bytes"/> bytes as hex, a repeating pattern seeded by <paramref name="seed"/>.</summary>
    private static string Hex(int bytes, int seed) =>
        Convert.ToHexString([.. Enumerable.Range(0, bytes).Select(i => (byte)((i * 7 + seed) % 251))]);

    private static string Notes(int i) => string.Concat(Enumerable.Repeat($"Note {i}. ", i * 12));

    private static string Abbreviate(string statement) => statement.Length <= 90 ? statement : statement[..87] + "...";

    private static string?[] RunThroughAce(string path, string[] statements, Action? beforeClose = null)
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
        beforeClose?.Invoke();
        return errors;
    }

    private static string?[] RunThroughLibRed(string path, string[] statements, Action? beforeClose = null)
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
        beforeClose?.Invoke();
        return errors;
    }

    /// <summary>Compares the two results of the same statements applied to <paramref name="originPath"/>: page
    /// counts, then every differing page — page 0 on its own, the rest grouped by page type and owner — with its
    /// differing byte count and the first few differences. Returns the report and how many pages differ.</summary>
    private static (string Report, int Pages) Compare(string originPath, string acePath, string noisePath, string libredPath)
    {
        byte[] origin = File.ReadAllBytes(originPath), ace = File.ReadAllBytes(acePath),
               noise = File.ReadAllBytes(noisePath), libred = File.ReadAllBytes(libredPath);
        JetFormatBase format = JetFormatBase.Detect(new MemoryStream(origin));
        int pageSize = format.PageSize;
        Dictionary<int, string> owners = Owners(acePath, libredPath);
        HashSet<int>[] masks = [.. Enumerable.Range(0, Math.Max(ace.Length, libred.Length) / pageSize).Select(_ => new HashSet<int>())];
        Mask(acePath, ace, masks);
        Mask(libredPath, libred, masks);

        int acePages = ace.Length / pageSize, libredPages = libred.Length / pageSize;
        int identical = 0, housekeeping = 0, differingPages = 0;
        var groups = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        for (int page = 0; page < Math.Max(acePages, libredPages); page++)
        {
            int at = page * pageSize;
            if (page >= acePages || page >= libredPages)
            {
                byte[] only = page >= acePages ? libred : ace;
                Add(groups, "pages in one file only", $"page {page}: {(page >= acePages ? "LibRed" : "ACE")} only, " +
                    $"type 0x{only[at]:X2} {Owner(owners, format, only, at)}");
                differingPages++;
                continue;
            }

            var differing = new List<int>();
            for (int i = 0; i < pageSize; i++)
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
                : $"type 0x{ace[at]:X2}{(ace[at] != libred[at] ? $"/0x{libred[at]:X2}" : "")} {Owner(owners, format, ace, at)}";
            string slots = ace[at] == 0x01
                ? "\n      slots " + string.Join(" ", Enumerable.Range(0, DataPage.ReadRowCount(ace.AsSpan(at, pageSize), format))
                    .Select(r => DataPage.ReadSlot(ace.AsSpan(at, pageSize), format, r))
                    .Select(s => $"{s.Offset:X3}/{(int)s.Flags:X4}"))
                : "";
            Add(groups, kind, $"page {page}: {differing.Count} bytes  {detail}{slots}{Runs(origin, ace, libred, at, differing)}");
        }

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"pages: before {origin.Length / pageSize}, ACE {acePages}, LibRed {libredPages}; identical {identical}; " +
            $"{housekeeping} byte(s) differ only where ACE's session alone writes (ace/libred shown)");
        foreach (var (kind, lines) in groups)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"-- {kind}: {lines.Count} page(s)");
            foreach (string line in lines) report.AppendLine(CultureInfo.InvariantCulture, $"   {line}");
        }
        return (report.ToString(), differingPages);
    }

    /// <summary>The page's differing bytes as runs — those within four bytes of each other joined, with sixteen bytes
    /// of context either side — the file before the statement, then ACE's line over LibRed's, for the first forty
    /// runs.</summary>
    private static string Runs(byte[] origin, byte[] ace, byte[] libred, int at, List<int> differing)
    {
        var text = new StringBuilder();
        int k = 0, runs = 0;
        while (k < differing.Count && runs++ < 40)
        {
            int start = differing[k], end = start;
            while (k + 1 < differing.Count && differing[k + 1] <= end + 4) end = differing[++k];
            k++;
            int from = Math.Max(0, start - 16), length = Math.Min(Math.Min(end + 17, 4096), ace.Length - at) - from;
            if (at + from + length <= origin.Length)
                text.Append(CultureInfo.InvariantCulture, $"\n      +0x{from:X3} WAS {Convert.ToHexString(origin, at + from, length)}");
            text.Append(CultureInfo.InvariantCulture, $"\n      +0x{from:X3} ACE {Convert.ToHexString(ace, at + from, length)}")
                .Append(CultureInfo.InvariantCulture, $"\n      +0x{from:X3} LIB {Convert.ToHexString(libred, at + from, length)}");
        }
        return text.ToString();
    }

    private static void Add(SortedDictionary<string, List<string>> groups, string kind, string line)
    {
        if (!groups.TryGetValue(kind, out var lines)) groups[kind] = lines = [];
        lines.Add(line);
    }

    /// <summary>What owns a page: for a data or index page the table whose TDEF page its header names, for a TDEF
    /// page its own table, where either file's catalog knows the name.</summary>
    private static string Owner(Dictionary<int, string> owners, JetFormatBase format, byte[] file, int at)
    {
        if (PageHeader.ReadType(file.AsSpan(at)) is PageType.TableDefinition or PageType.ReleasedTableDefinition)
            return owners.TryGetValue(at / format.PageSize, out string? self) ? $"({self})" : "";
        if (PageHeader.ReadType(file.AsSpan(at)) is not (PageType.DataPage or PageType.IntermediateIndexPage or PageType.LeafIndexPage))
            return "";
        ReadOnlySpan<byte> page = file.AsSpan(at, format.PageSize);
        uint ownerField = PageHeader.ReadType(page) == PageType.DataPage
            ? DataPage.ReadOwner(page, format)
            : (uint)IndexTree.ReadOwner(page, format);
        if (ownerField == JetFormatBase.LongValuePageMarker) return "(long values)";
        int owner = (int)ownerField;
        return owners.TryGetValue(owner, out string? name) ? $"({name})" : $"(tdef {owner})";
    }

    private static Dictionary<int, string> Owners(params string[] paths)
    {
        var owners = new Dictionary<int, string>();
        foreach (string path in paths)
        {
            using var database = JetDatabase.Open(path, readOnly: true);
            foreach (TableDefinition table in database.Catalog.Tables) owners.TryAdd(table.DefinitionPage, table.Name);
        }
        return owners;
    }

    /// <summary>Masks the bytes known to differ for reasons other than the format: page 0's user commit-byte table,
    /// a count of each lock-file user's committed writes that LibRed, keeping no lock file, leaves alone
    /// (page-00-database.md §2.2); each MSysObjects row's wall-clock DateCreate and DateUpdate; and every data page's
    /// write stamp.</summary>
    private static void Mask(string path, byte[] file, HashSet<int>[] masks)
    {
        using var database = JetDatabase.Open(path, readOnly: true);
        JetFormatBase format = database.Format;
        int pageSize = format.PageSize;

        for (int i = format.CommitByteTableOffset; i < pageSize; i++) masks[0].Add(i);

        TableDefinition objects = database.Catalog.FindTable("MSysObjects")!;
        int[] dates = [.. new[] { "DateCreate", "DateUpdate" }.Select(c => objects.FindColumn(c)!.FixedOffset)];

        for (int page = 1; page < file.Length / pageSize; page++)
        {
            int at = page * pageSize;
            if (PageHeader.ReadType(file.AsSpan(at)) != PageType.DataPage) continue;
            for (int i = format.DataChainStampOffset; i < format.DataChainStampOffset + sizeof(int); i++) masks[page].Add(i);
            ReadOnlySpan<byte> bytes = file.AsSpan(at, pageSize);
            if ((int)DataPage.ReadOwner(bytes, format) != objects.DefinitionPage) continue;

            // Every record on the page, a relocated row's hidden target included (flagged deleted but not empty) —
            // only a forwarding pointer and a zero-length tombstone hold no row.
            int rows = DataPage.ReadRowCount(bytes, format);
            int end = pageSize;
            var stamps = new List<byte[]>();
            for (int row = 0; row < rows; row++)
            {
                (int start, RowSlotFlags flags) = DataPage.ReadSlot(bytes, format, row);
                int length = end - start;
                if (start < end) end = start;
                if ((flags & RowSlotFlags.Overflow) != 0 || length <= 0) continue;
                foreach (int offset in dates)
                {
                    int date = start + format.RowColumnCountSize + offset;
                    stamps.Add(bytes.Slice(date, 8).ToArray());
                    for (int i = 0; i < 8; i++) masks[page].Add(date + i);
                }
            }

            // And the stale copy of a record a rewrite left in the free space below the rows: both engines leave the
            // same image there, the clock in it as in the live row it came from.
            int freeStart = DataPage.DirectoryEnd(format, rows);
            for (int p = freeStart; p + 8 <= end; p++)
                foreach (byte[] stamp in stamps)
                    if (bytes.Slice(p, 8).SequenceEqual(stamp))
                        for (int i = 0; i < 8; i++) masks[page].Add(p + i);
        }

        // A chained long value's descriptor carries the same write stamp as the chain's first page, bytes 8–11 of
        // its twelve (long-values.md).
        foreach (TableDefinition definition in database.Catalog.Tables)
        {
            if (!definition.Columns.Any(c => c.Type is JetDataType.Memo or JetDataType.Ole)) continue;
            Table table = database.OpenTable(definition.Name);
            foreach ((RowId id, _) in table.Rows().WithIds())
            {
                var page = new DataPage();
                page.Read(table.Channel.ReadPageShared(id.Page), table.Channel.Format);
                if (page.Rows[id.Row] is not { IsDeleted: false, HasOverflow: false } slot) continue;
                ReadOnlySpan<byte> row = page.GetRow(id.Row);
                foreach (byte[] descriptor in RowCodec.LongValueDescriptors(definition, table.Channel.Format, row).Values)
                {
                    if (descriptor.Length < format.LongValueDescriptorSize
                        || LongValueStore.Read(descriptor, format).Storage != LongValueStore.StorageKind.Chained) continue;
                    int at = row.IndexOf(descriptor);
                    int stamp = format.LongValueDescriptorChainStampOffset;
                    if (at >= 0)
                        for (int i = stamp; i < stamp + sizeof(int); i++) masks[id.Page].Add(slot.Offset + at + i);
                }
            }
        }
    }

    /// <summary>Gives each query in LibRed's file the <c>MSysObjects.Lv</c> ACE's has, through LibRed's own row update.
    /// It is ACE's compiled plan, a memory image with heap and code addresses in it that ACE rewrites on every run
    /// (system-catalog.md), so its bytes can never match; only they are taken from ACE — the row's descriptor, the
    /// LVAL page and the maps the value brings are still LibRed's to write, and compared.</summary>
    private static void CopyQueryPlans(string acePath, string libredPath)
    {
        Dictionary<string, byte[]> plans = new(StringComparer.OrdinalIgnoreCase);
        using (var ace = JetDatabase.Open(acePath, readOnly: true))
            foreach ((string name, byte[]? plan, _, _) in QueryRows(ace))
                if (plan is not null) plans[name] = plan;

        using var libred = JetDatabase.Open(libredPath, readOnly: false);
        foreach ((string name, byte[]? plan, RowId id, object?[] values) in QueryRows(libred))
        {
            if (!plans.TryGetValue(name, out byte[]? acePlan) || plan is not null && plan.AsSpan().SequenceEqual(acePlan)) continue;
            int lv = libred.Catalog.FindTable("MSysObjects")!.FindColumn("Lv")!.Index;
            values[lv] = acePlan;
            libred.OpenTable("MSysObjects").Update(id, values, new HashSet<int> { lv });
        }
    }

    /// <summary>Every stored query's MSysObjects row: its name, its <c>Lv</c>, its id and its values.</summary>
    private static List<(string Name, byte[]? Plan, RowId Id, object?[] Values)> QueryRows(JetDatabase database)
    {
        TableDefinition objects = database.Catalog.FindTable("MSysObjects")!;
        int name = objects.FindColumn("Name")!.Index, type = objects.FindColumn("Type")!.Index, lv = objects.FindColumn("Lv")!.Index;
        return [.. database.OpenTable("MSysObjects").Rows().WithIds()
            .Where(r => r.Values[type] is short t && t == 5)
            .Select(r => ((string)r.Values[name]!, r.Values[lv] as byte[], r.Id, r.Values))];
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
