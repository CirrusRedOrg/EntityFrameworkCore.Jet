using System.Data.OleDb;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// The ACE half of ReferentialActionTests: the two cascade shapes LibRed cannot decide on its own — a table
// whose parent and child ends are the SAME table, so a cascaded row is also a row the statement itself is
// updating, and a cascade that has to carry on into a grandchild. The same statements run through ACE and
// through LibRed on copies of one file, and the rows are read back through ACE either way, so a row that
// disagrees with its index shows up as a seek that misses.
[Collection(AceCollection.Name)]
public class ReferentialActionAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    private static string Northwind => Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    private static readonly string[] SelfReference =
    [
        "CREATE TABLE Emp (Id LONG CONSTRAINT pk PRIMARY KEY, MgrId LONG, "
        + "CONSTRAINT fk FOREIGN KEY (MgrId) REFERENCES Emp (Id) ON UPDATE CASCADE)",
        "INSERT INTO Emp (Id, MgrId) VALUES (1, NULL)",
        "INSERT INTO Emp (Id, MgrId) VALUES (2, 1)",
        "UPDATE Emp SET Id = Id + 100",
    ];

    private static readonly string[] TwoLevels =
    [
        "CREATE TABLE P (Id LONG CONSTRAINT pkP PRIMARY KEY)",
        "CREATE TABLE C (Id LONG CONSTRAINT pkC PRIMARY KEY, PId LONG, "
        + "CONSTRAINT fkC FOREIGN KEY (PId) REFERENCES P (Id) ON UPDATE CASCADE)",
        "CREATE TABLE G (Id LONG CONSTRAINT pkG PRIMARY KEY, CId LONG, "
        + "CONSTRAINT fkG FOREIGN KEY (CId) REFERENCES C (Id) ON UPDATE CASCADE)",
        "INSERT INTO P (Id) VALUES (1)",
        "INSERT INTO C (Id, PId) VALUES (10, 1)",
        "INSERT INTO G (Id, CId) VALUES (100, 10)",
        "UPDATE P SET Id = 5",
    ];

    [Fact]
    public void A_self_referencing_cascade_update_leaves_what_ace_leaves()
    {
        string ace = Apply(SelfReference, throughAce: true);
        string libred = Apply(SelfReference, throughAce: false);

        // Read back through ACE: the rows, then the FK index, which is where a row rewritten from the pre-cascade
        // snapshot parts company with the entry the cascade already moved.
        string aceRows = Read(ace, "SELECT Id, MgrId FROM Emp ORDER BY Id");
        string libredRows = Read(libred, "SELECT Id, MgrId FROM Emp ORDER BY Id");
        string aceSeek = Read(ace, "SELECT Id FROM Emp WHERE MgrId = 101");
        string libredSeek = Read(libred, "SELECT Id FROM Emp WHERE MgrId = 101");
        output.WriteLine($"ACE    rows [{aceRows}] seek MgrId=101 [{aceSeek}]");
        output.WriteLine($"LibRed rows [{libredRows}] seek MgrId=101 [{libredSeek}]");

        Assert.Equal("101,null | 102,101", aceRows);
        Assert.Equal(aceRows, libredRows);
        Assert.Equal(aceSeek, libredSeek);
    }

    [Fact]
    public void A_cascade_update_reaches_a_grandchild_as_it_does_in_ace()
    {
        string ace = Apply(TwoLevels, throughAce: true);
        string libred = Apply(TwoLevels, throughAce: false);

        string aceRows = Read(ace, "SELECT P.Id, C.Id, C.PId, G.CId FROM (P INNER JOIN C ON P.Id = C.PId) INNER JOIN G ON C.Id = G.CId");
        string libredRows = Read(libred, "SELECT P.Id, C.Id, C.PId, G.CId FROM (P INNER JOIN C ON P.Id = C.PId) INNER JOIN G ON C.Id = G.CId");
        output.WriteLine($"ACE    [{aceRows}]");
        output.WriteLine($"LibRed [{libredRows}]");

        Assert.Equal("5,10,5,10", aceRows);
        Assert.Equal(aceRows, libredRows);
    }

    /// <summary>Runs the statements on a fresh copy, through ACE or through LibRed, and returns the copy's path.</summary>
    private static string Apply(string[] statements, bool throughAce)
    {
        string path = TemporaryDatabase.CopyPath(Northwind, throughAce ? "refaction-ace-" : "refaction-lib-");
        if (throughAce)
        {
            using OleDbConnection connection = AceTestDatabase.Open(path);
            foreach (string statement in statements)
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }
        }
        else
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);
            foreach (string statement in statements) engine.ExecuteNonQuery(statement);
        }
        return path;
    }

    /// <summary>The rows ACE reads for a query, as <c>a,b | c,d</c>.</summary>
    private static string Read(string path, string sql)
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using OleDbDataReader reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(string.Join(",", Enumerable.Range(0, reader.FieldCount)
                .Select(i => reader.IsDBNull(i) ? "null" : Convert.ToString(reader.GetValue(i)))));
        return string.Join(" | ", rows);
    }
}
