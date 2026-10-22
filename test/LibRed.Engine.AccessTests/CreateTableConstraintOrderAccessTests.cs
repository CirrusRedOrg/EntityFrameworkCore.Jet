using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A table constraint may be written anywhere among CREATE TABLE's column definitions, but ACE resolves each one
/// as it reaches it, against what the statement has declared so far: a constraint naming a column written after it
/// is refused as one naming a column that does not exist, and a self-reference to a key written after it as one
/// to a key the table does not have. LibRed refuses the same statements with the same message, leaving no table.
/// (Where the accepted forms put each index's usage-map row is <see cref="CreateTableUsageMapOrderAccessTests"/>.)
/// </summary>
[Collection(AceCollection.Name)]
public class CreateTableConstraintOrderAccessTests(ITestOutputHelper output)
{
    [Theory]
    // A constraint naming a column written after it…
    [InlineData("CREATE TABLE T (CONSTRAINT pk PRIMARY KEY (Id), Id LONG)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT pk PRIMARY KEY (B), B LONG)")]
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT u UNIQUE (A, B), B LONG)")]
    [InlineData("CREATE TABLE T (CONSTRAINT fk FOREIGN KEY (A) REFERENCES Employees (EmployeeID), M MEMO, A LONG)")]
    // …is refused as one naming a column that is never declared, or a statement with no columns at all.
    [InlineData("CREATE TABLE T (A LONG, CONSTRAINT pk PRIMARY KEY (Z), M MEMO)")]
    [InlineData("CREATE TABLE T (CONSTRAINT pk PRIMARY KEY (Id))")]
    // A self-reference naming a key column written after it, at table and at column level.
    [InlineData("CREATE TABLE T (Id LONG PRIMARY KEY, P LONG, CONSTRAINT fk FOREIGN KEY (P) REFERENCES T (Q), Q LONG UNIQUE)")]
    [InlineData("CREATE TABLE T (P LONG REFERENCES T (Id), Id LONG PRIMARY KEY)")]
    // A foreign key's own columns are checked before the parent's columns, its key and the type match…
    [InlineData("CREATE TABLE T (CONSTRAINT fk FOREIGN KEY (A) REFERENCES Employees (Nope), A LONG)")]
    [InlineData("CREATE TABLE T (Id LONG PRIMARY KEY, CONSTRAINT fk FOREIGN KEY (P) REFERENCES T, P LONG)")]
    [InlineData("CREATE TABLE T (Id LONG, CONSTRAINT fk FOREIGN KEY (P) REFERENCES T (Id), P LONG, CONSTRAINT pk PRIMARY KEY (Id))")]
    // …but after its reference: a self-reference with no column list and its primary key written after it is
    // refused for the key, though the column it names is written after it too.
    [InlineData("CREATE TABLE T (Id LONG, CONSTRAINT fk FOREIGN KEY (P) REFERENCES T, P LONG, CONSTRAINT pk PRIMARY KEY (Id))")]
    public void Libred_refuses_a_constraint_naming_what_is_declared_after_it_as_ace_does(string sql)
    {
        (string ace, string libred) = Outcomes(sql);
        Assert.StartsWith("refused", ace);
        Assert.Equal(ace, libred);
    }

    /// <summary>A self-reference whose referenced key is declared after it. Both engines refuse it, but not with
    /// the same words: LibRed reports the missing key in its own message here, as it does for a parent table with
    /// no index over the referenced columns.</summary>
    [Fact]
    public void Libred_refuses_a_self_reference_to_a_key_declared_after_it()
    {
        (string ace, string libred) = Outcomes(
            "CREATE TABLE T (Id LONG, P LONG, CONSTRAINT fk FOREIGN KEY (P) REFERENCES T (Id), CONSTRAINT pk PRIMARY KEY (Id))");
        Assert.Contains("No unique index found for the referenced field of the primary table", ace);
        Assert.Contains("has no index over (Id)", libred);
        Assert.EndsWith("; no table T", ace);
        Assert.EndsWith("; no table T", libred);
    }

    private (string Ace, string Libred) Outcomes(string sql)
    {
        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string ace = TemporaryDatabase.CopyPath(northwind, "conorder-ace-");
        string libred = TemporaryDatabase.CopyPath(northwind, "conorder-lib-");
        try
        {
            string aceOutcome = Run(() =>
            {
                using OleDbConnection connection = AceTestDatabase.Open(ace);
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }) + "; " + Left(ace);
            string libredOutcome = Run(() =>
            {
                using var db = JetDatabase.Open(libred, readOnly: false);
                new QueryEngine(db).ExecuteNonQuery(sql);
            }) + "; " + Left(libred);

            output.WriteLine($"ACE    {aceOutcome}\nLibRed {libredOutcome}");
            return (aceOutcome, libredOutcome);
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    /// <summary>Whether the statement was refused, and the refusal's message.</summary>
    private static string Run(Action execute)
    {
        try { execute(); }
        catch (Exception e) { return $"refused: {e.GetBaseException().Message}"; }
        return "accepted";
    }

    /// <summary>Whether the statement left a table T behind.</summary>
    private static string Left(string path)
    {
        using var db = JetDatabase.Open(path);
        return db.Catalog.FindTable("T") is null ? "no table T" : "table T";
    }
}
