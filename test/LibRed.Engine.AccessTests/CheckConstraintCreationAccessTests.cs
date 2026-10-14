using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// ACE validates a CHECK when the constraint is created, in CREATE TABLE and ALTER TABLE ADD CONSTRAINT alike, not
/// when a row first meets it: an expression that does not parse, that names a column the table does not have — in
/// CREATE TABLE, one not declared before the constraint — even in a branch that never runs, that uses an aggregate,
/// or whose subquery reads a table that does not exist, is refused with "Syntax error in CHECK constraint clause.",
/// leaving nothing behind. LibRed refuses the same statements with the same message. A column-level CHECK is not ACE
/// syntax at all.
/// </summary>
[Collection(AceCollection.Name)]
public class CheckConstraintCreationAccessTests(ITestOutputHelper output)
{
    [Theory]
    // Accepted, and enforced as ACE enforces them.
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (A > 0))", "INSERT INTO T VALUES (1)", "INSERT INTO T VALUES (-1)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM Employees) > 0))", "INSERT INTO T VALUES (1)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (A IN (SELECT EmployeeID FROM Employees)))", "INSERT INTO T VALUES (1)", "INSERT INTO T VALUES (999)")]
    // A name the row does not have: unknown, declared after the constraint, another table's, or bracketed like a
    // parameter — and one in an IIf branch that never runs.
    [InlineData("CREATE TABLE T (B LONG, CONSTRAINT ck CHECK (Z > 0))")]
    [InlineData("CREATE TABLE T (CONSTRAINT ck CHECK (B > 0), B LONG)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (A > Employees.EmployeeID))")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (A > [Enter value]))")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (IIF(True, 1, Z) = 1))")]
    // An expression that does not parse.
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (A >))")]
    [InlineData("CREATE TABLE T (ID LONG PRIMARY KEY, Amount DOUBLE, CONSTRAINT CheckAmount CHECK (Amount > 0 unexpected))")]
    // An aggregate, and a subquery over a table that does not exist.
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK (Sum(A) > 0))")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT ck CHECK ((SELECT COUNT(*) FROM Nope) > 0))")]
    // The same when the constraint is added to an existing table.
    [InlineData("CREATE TABLE T (A LONG)", "ALTER TABLE T ADD CONSTRAINT ck CHECK (Z > 0)")]
    [InlineData("CREATE TABLE T (A LONG)", "ALTER TABLE T ADD CONSTRAINT ck CHECK (A >)")]
    public void Libred_validates_a_check_when_it_is_created_as_ace_does(params string[] steps)
    {
        (string ace, string libred) = Outcomes(steps);
        Assert.Equal(ace, libred);
    }

    [Theory]
    [InlineData("CREATE TABLE T (A LONG CHECK (A > 0))")]
    [InlineData("CREATE TABLE T (A LONG CONSTRAINT ck CHECK (A > 0))")]
    [InlineData("CREATE TABLE T (B LONG, A LONG CONSTRAINT ck CHECK (B > 0))")]
    [InlineData("CREATE TABLE T (A LONG CONSTRAINT ck CHECK (A IN (SELECT EmployeeID FROM Employees)))")]
    [InlineData("CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY, `Age` INTEGER CONSTRAINT `CK_Age` CHECK (`Age` > 0 AND `Age` < 200))")]
    [InlineData("CREATE TABLE T (A LONG)", "ALTER TABLE T ADD COLUMN B LONG CONSTRAINT ck CHECK (B > 0)")]
    public void Libred_refuses_a_column_level_check_as_ace_does(params string[] steps)
    {
        (string ace, string libred) = Outcomes(steps);
        string refused = $"refused at step {steps.Length}: ";
        Assert.StartsWith(refused + "Syntax error in field definition.", ace);
        Assert.StartsWith(refused, libred);
        Assert.Equal(ace.Split(" | ")[1], libred.Split(" | ")[1]);
    }

    private (string Ace, string Libred) Outcomes(string[] steps)
    {
        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string ace = TemporaryDatabase.CopyPath(northwind, "chkcreate-ace-");
        string libred = TemporaryDatabase.CopyPath(northwind, "chkcreate-lib-");
        try
        {
            string aceOutcome = Run(steps, sql =>
            {
                using OleDbConnection connection = AceTestDatabase.Open(ace);
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }) + " | " + Left(ace);
            string libredOutcome = Run(steps, sql =>
            {
                using var db = JetDatabase.Open(libred, readOnly: false);
                new QueryEngine(db).ExecuteNonQuery(sql);
            }) + " | " + Left(libred);

            output.WriteLine($"ACE    {aceOutcome}\nLibRed {libredOutcome}");
            return (aceOutcome, libredOutcome);
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    /// <summary>Runs the steps until one is refused: which step, and the refusal's message.</summary>
    private static string Run(string[] steps, Action<string> execute)
    {
        for (int step = 0; step < steps.Length; step++)
        {
            try { execute(steps[step]); }
            catch (Exception e) { return $"refused at step {step + 1}: {e.GetBaseException().Message}"; }
        }
        return "accepted";
    }

    /// <summary>What the statements left: table T's columns and CHECK constraints, or its absence.</summary>
    private static string Left(string path)
    {
        using var db = JetDatabase.Open(path);
        var table = db.Catalog.FindTable("T");
        return table is null ? "no table T"
            : $"T [{string.Join(",", table.Columns.Select(c => c.Name))}] checks [{string.Join(", ", table.CheckConstraints.Select(c => $"{c.Name}={c.Expression}"))}]";
    }
}
