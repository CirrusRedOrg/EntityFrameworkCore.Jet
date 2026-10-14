using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// How a CHECK holds once it exists, against ACE.
/// <list type="bullet">
/// <item>It is evaluated after each row is written, one row at a time in the order the statement writes them, so a
/// subquery over the table sees the row; a violation takes the whole statement back.</item>
/// <item>A DELETE evaluates it after each deleted row is gone, against a row whose every column is Null.</item>
/// <item>A row a cascade wrote — ON UPDATE CASCADE, ON DELETE SET NULL, ON DELETE CASCADE — is refused in other
/// words, and an expression that cannot be evaluated with "Error evaluating … CHECK constraint."</item>
/// <item>The row being checked is named by U+FFFE in the stored text: a column qualified with the table's own name
/// loses the qualifier, or inside a subquery takes U+FFFE, unless that subquery reads the table itself.</item>
/// </list>
/// </summary>
[Collection(AceCollection.Name)]
public class CheckConstraintEnforcementAccessTests(ITestOutputHelper output)
{
    private const string Parent = "CREATE TABLE P (Id LONG PRIMARY KEY)";

    [Theory]
    // After the write: a subquery over the table counts the row being written.
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM T) < 2))", "INSERT INTO T VALUES (1)", "INSERT INTO T VALUES (2)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT MAX(A) FROM T) <= 10))", "INSERT INTO T VALUES (5)", "UPDATE T SET A = 20")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM T WHERE A > 5) < 1))", "INSERT INTO T VALUES (6)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (NOT EXISTS (SELECT A FROM T GROUP BY A HAVING COUNT(*) > 1)))", "INSERT INTO T VALUES (1)", "INSERT INTO T VALUES (1)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM T) >= 1))", "INSERT INTO T VALUES (1)", "INSERT INTO T VALUES (2)", "DELETE FROM T WHERE A = 2", "DELETE FROM T")]
    // One row at a time, in the order the statement writes them, and the whole statement undone.
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM T) < 3))", "INSERT INTO T (A) SELECT EmployeeID FROM Employees")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT SUM(A) FROM T) <= 20))", "INSERT INTO T (A) SELECT EmployeeID FROM Employees WHERE EmployeeID <= 6")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM T WHERE A = 2) <= 1))", "INSERT INTO T VALUES (1)", "INSERT INTO T VALUES (2)", "UPDATE T SET A = A + 1")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM T WHERE A = 2) <= 1))", "INSERT INTO T VALUES (2)", "INSERT INTO T VALUES (1)", "UPDATE T SET A = A + 1")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM T WHERE A = 1) <= 1))", "INSERT INTO T VALUES (1)", "INSERT INTO T VALUES (2)", "INSERT INTO T VALUES (3)", "UPDATE T SET A = 1 WHERE A > 1", "INSERT INTO T VALUES (1)")]
    // Only the row written is checked, not the rows already there.
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (A >= (SELECT COUNT(*) FROM T)))", "INSERT INTO T VALUES (1)", "INSERT INTO T VALUES (5)")]
    [InlineData("CREATE TABLE T (K LONG, A LONG, CONSTRAINT ck CHECK (A >= (SELECT MAX(K) FROM T)))", "INSERT INTO T VALUES (1, 5)", "INSERT INTO T VALUES (2, 5)", "UPDATE T SET K = 7 WHERE K = 2")]
    // A DELETE checks a row of Nulls, once per row it deletes.
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (A < 10 OR (SELECT COUNT(*) FROM T) >= 2))", "INSERT INTO T VALUES (5)", "INSERT INTO T VALUES (20)", "DELETE FROM T WHERE A = 5")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (A < 10 OR (SELECT COUNT(*) FROM T) >= 2))", "INSERT INTO T VALUES (5)", "INSERT INTO T VALUES (20)", "DELETE FROM T WHERE A = 20")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (A > 0))", "INSERT INTO T VALUES (5)", "DELETE FROM T")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM T) <> 2))", "INSERT INTO T VALUES (1)", "INSERT INTO T VALUES (2)", "INSERT INTO T VALUES (3)", "DELETE FROM T")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM T) >= 1))", "INSERT INTO T VALUES (1)", "DELETE FROM T WHERE A = 99")]
    // A DELETE with no WHERE, which empties the table in one go, against one passing through a state the check refuses.
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM T) <> 2 OR (SELECT MAX(A) FROM T) > 100))",
        "INSERT INTO T VALUES (300)", "INSERT INTO T VALUES (1)", "INSERT INTO T VALUES (2)", "DELETE FROM T")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM T) <> 2 OR (SELECT MAX(A) FROM T) > 100))",
        "INSERT INTO T VALUES (300)", "INSERT INTO T VALUES (1)", "INSERT INTO T VALUES (2)", "DELETE FROM T WHERE A > 0")]
    // A cascade's row is refused in a cascade's words.
    [InlineData(Parent, "CREATE TABLE T (A LONG, P LONG REFERENCES P (Id) ON DELETE CASCADE, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM T) >= 1))", "INSERT INTO P VALUES (1)", "INSERT INTO P VALUES (2)", "INSERT INTO T VALUES (1, 1)", "INSERT INTO T VALUES (2, 2)", "DELETE FROM P WHERE Id = 1", "DELETE FROM P")]
    [InlineData(Parent, "CREATE TABLE T (A LONG, P LONG REFERENCES P (Id) ON UPDATE CASCADE, CONSTRAINT ck CHECK (P < 5))", "INSERT INTO P VALUES (1)", "INSERT INTO T VALUES (1, 1)", "UPDATE P SET Id = 9 WHERE Id = 1")]
    [InlineData(Parent, "CREATE TABLE T (A LONG, P LONG REFERENCES P (Id) ON DELETE SET NULL, CONSTRAINT ck CHECK (P IS NOT NULL))", "INSERT INTO P VALUES (1)", "INSERT INTO T VALUES (1, 1)", "DELETE FROM P")]
    // An expression that cannot be evaluated, a cascade's included.
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (1/A > 0))", "INSERT INTO T VALUES (0)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (A > 0 OR 1/A > 0))", "INSERT INTO T VALUES (0)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (CInt(A) > 0))", "INSERT INTO T VALUES (100000)")]
    [InlineData("CREATE TABLE T (S TEXT(10), CONSTRAINT ck CHECK (S > 5))", "INSERT INTO T VALUES ('10')", "INSERT INTO T VALUES ('abc')")]
    [InlineData(Parent, "CREATE TABLE T (A LONG, P LONG REFERENCES P (Id) ON DELETE CASCADE, CONSTRAINT ck CHECK (1 / (SELECT COUNT(*) FROM T) > 0))", "INSERT INTO P VALUES (1)", "INSERT INTO T VALUES (1, 1)", "DELETE FROM P")]
    // A backtick name, a 0x literal and an integer literal past a Long, each as ACE evaluates it.
    [InlineData("CREATE TABLE T (S TEXT(10), CONSTRAINT ck CHECK (`S` <> 'x'))", "INSERT INTO T VALUES ('x')", "INSERT INTO T VALUES ('y')")]
    [InlineData("CREATE TABLE T (Qty LONG)", "ALTER TABLE T ADD CONSTRAINT ck CHECK (`Qty` > 0)", "INSERT INTO T VALUES (5)", "INSERT INTO T VALUES (-5)")]
    [InlineData("CREATE TABLE T (Qty LONG, CONSTRAINT ck CHECK (`Qty` > 0))", "INSERT INTO T VALUES (5)", "INSERT INTO T VALUES (-5)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (A > 0x10))", "INSERT INTO T VALUES (5)", "INSERT INTO T VALUES (20)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (A < 100000000000000000000))", "INSERT INTO T VALUES (5)")]
    // LIKE as written and as re-formed to ALike match the same values.
    [InlineData("CREATE TABLE T (S TEXT(10), CONSTRAINT ck CHECK (S LIKE 'a*'))", "INSERT INTO T VALUES ('abc')", "INSERT INTO T VALUES ('a*')")]
    [InlineData("CREATE TABLE T (S TEXT(10), CONSTRAINT ck CHECK (T.S LIKE 'a*'))", "INSERT INTO T VALUES ('abc')", "INSERT INTO T VALUES ('a*')")]
    [InlineData("CREATE TABLE T (S TEXT(10), CONSTRAINT ck CHECK (S LIKE 'a%'))", "INSERT INTO T VALUES ('abc')", "INSERT INTO T VALUES ('xbc')")]
    [InlineData("CREATE TABLE T (S TEXT(10), CONSTRAINT ck CHECK (T.S LIKE 'a%'))", "INSERT INTO T VALUES ('abc')", "INSERT INTO T VALUES ('xbc')")]
    // The row named by U+FFFE inside a subquery, and the table's own name left alone where the subquery reads it.
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM Employees WHERE EmployeeID = T.A) > 0))", "INSERT INTO T VALUES (1)", "INSERT INTO T VALUES (999)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM Employees WHERE T.A = 1) > 0))")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM T AS X WHERE X.A > T.A) < 1))", "INSERT INTO T VALUES (5)", "INSERT INTO T VALUES (3)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (EXISTS (SELECT * FROM Employees WHERE EXISTS (SELECT * FROM Orders WHERE Orders.EmployeeID = T.A))))", "INSERT INTO T VALUES (1)", "INSERT INTO T VALUES (999)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM T WHERE T.A > 5) < 1))", "INSERT INTO T VALUES (6)")]
    public void Libred_enforces_a_check_as_ace_does(params string[] steps)
    {
        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string ace = TemporaryDatabase.CopyPath(northwind, "chkenforce-ace-");
        string libred = TemporaryDatabase.CopyPath(northwind, "chkenforce-lib-");
        try
        {
            string aceOutcome = Run(steps, Ace(ace)) + " | " + Left(ace);
            string libredOutcome = Run(steps, Libred(libred)) + " | " + Left(libred);
            output.WriteLine($"ACE    {aceOutcome}\nLibRed {libredOutcome}");
            Assert.Equal(aceOutcome, libredOutcome);
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    /// <summary>
    /// A CHECK naming its table's own columns with the table's name, created by one engine and written by the other.
    /// Where it drops such a qualifier ACE also rewrites the rest of the text, which LibRed keeps as written, so the
    /// stored text is not compared: what has to hold is that each engine enforces the other's CHECK exactly as ACE
    /// enforces its own.
    /// </summary>
    [Theory]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (T.A > 0))")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (T.A > 0 AND A < 500))")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (t.a IN (1, 999)))")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (T.A IN (SELECT EmployeeID FROM Employees)))")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM Employees WHERE EmployeeID = T.A) > 0))")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (`A` > 0))")]
    public void Each_engine_enforces_the_check_the_other_created(string create)
    {
        string[] inserts = ["INSERT INTO T VALUES (1)", "INSERT INTO T VALUES (-1)", "INSERT INTO T VALUES (999)"];
        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string aceOwn = TemporaryDatabase.CopyPath(northwind, "chkcross-ace-");
        string aceOnLibred = TemporaryDatabase.CopyPath(northwind, "chkcross-acelib-");
        string libredOnAce = TemporaryDatabase.CopyPath(northwind, "chkcross-libace-");
        try
        {
            Ace(aceOwn)(create);
            Libred(aceOnLibred)(create);
            Ace(libredOnAce)(create);
            string expected = Run(inserts, Ace(aceOwn)) + " | " + Rows(aceOwn);
            string aceWritingLibred = Run(inserts, Ace(aceOnLibred)) + " | " + Rows(aceOnLibred);
            string libredWritingAce = Run(inserts, Libred(libredOnAce)) + " | " + Rows(libredOnAce);
            output.WriteLine($"ACE on ACE       {expected}\nACE on LibRed    {aceWritingLibred}\nLibRed on ACE    {libredWritingAce}");
            Assert.Equal(expected, aceWritingLibred);
            Assert.Equal(expected, libredWritingAce);
        }
        finally
        {
            TemporaryDatabase.Delete(aceOwn);
            TemporaryDatabase.Delete(aceOnLibred);
            TemporaryDatabase.Delete(libredOnAce);
        }
    }

    private static Action<string> Ace(string path) => sql =>
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    };

    private static Action<string> Libred(string path) => sql =>
    {
        using var db = JetDatabase.Open(path, readOnly: false);
        new QueryEngine(db).ExecuteNonQuery(sql);
    };

    /// <summary>Each step's outcome: "ok", or the refusal's message — its own, not an inner exception's: LibRed keeps
    /// the evaluator's error inside the one it reports in ACE's words.</summary>
    private static string Run(string[] steps, Action<string> execute)
    {
        var parts = new List<string>();
        foreach (string step in steps)
        {
            try { execute(step); parts.Add("ok"); }
            catch (Exception e) { parts.Add($"refused: {e.Message}"); }
        }
        return string.Join("; ", parts);
    }

    /// <summary>What the statements left: table T's rows and stored CHECK constraints.</summary>
    private static string Left(string path)
    {
        using var db = JetDatabase.Open(path);
        var table = db.Catalog.FindTable("T");
        return table is null ? "no table T"
            : $"{Rows(path)} checks [{string.Join(", ", table.CheckConstraints.Select(c => $"{c.Name}={c.Expression}"))}]";
    }

    private static string Rows(string path)
    {
        using var db = JetDatabase.Open(path);
        return $"rows [{string.Join(" ", new QueryEngine(db).ExecuteQuery("SELECT * FROM T").Rows.Select(r => string.Join(",", r)))}]";
    }
}
