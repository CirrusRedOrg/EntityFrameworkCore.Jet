using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The text a CHECK constraint is stored as, and the qualifier spellings refused before it is, against ACE. A CHECK
/// is kept as written unless it qualifies a column with its own table's name outside a subquery: ACE then takes the
/// qualifier off and writes the whole expression out afresh — its own spacing, keyword spelling, brackets, numbers and
/// dates — with any subquery as written. ACE refuses the table's name as a qualifier unless bare and joined by '.',
/// space about a qualifier's separator, and a hex literal.
/// </summary>
[Collection(AceCollection.Name)]
public class CheckConstraintTextAccessTests(ITestOutputHelper output)
{
    [Theory]
    // Kept as written: no qualifier, or one only inside a subquery.
    [InlineData("A  >  0 and  [A] < 5")]
    [InlineData("[A] > 0")]
    [InlineData("(SELECT COUNT(*) FROM Employees WHERE EmployeeID = T.A) > 0 and A>0")]
    [InlineData("(SELECT COUNT(*) FROM Employees WHERE [Employees].[EmployeeID] > 0) > 0")]
    // Qualifier spellings ACE refuses.
    [InlineData("[T].[A] > 0")]
    [InlineData("[T].A > 0")]
    [InlineData("`T`.A > 0")]
    [InlineData("[t].[a] > 0")]
    [InlineData("T!A > 0")]
    [InlineData("T![A] > 0")]
    [InlineData("T .A > 0")]
    [InlineData("T. A > 0")]
    [InlineData("T . A > 0")]
    [InlineData("EXISTS (SELECT * FROM Employees WHERE EmployeeID = [T].A)")]
    [InlineData("EXISTS (SELECT * FROM Employees WHERE EmployeeID = T.[A])")]
    [InlineData("EXISTS (SELECT * FROM Employees WHERE EmployeeID = T.`A`)")]
    [InlineData("EXISTS (SELECT * FROM Employees WHERE EmployeeID = [T].[A])")]
    [InlineData("EXISTS (SELECT * FROM Employees WHERE EmployeeID = T!A)")]
    [InlineData("EXISTS (SELECT * FROM Employees WHERE EmployeeID = T .A)")]
    [InlineData("(SELECT COUNT(*) FROM Employees WHERE Employees .EmployeeID > 0) > 0")]
    [InlineData("A > &H10")]
    [InlineData("A > 0x10")]
    [InlineData("T.A > 0x10")]
    [InlineData("`A` > 0")]
    [InlineData("A > 100000000000000000000")]
    [InlineData("(SELECT COUNT(*) FROM Employees WHERE EmployeeID > &H1) > 0")]
    // Re-formed: operators and keywords.
    [InlineData("T.A = 5")]
    [InlineData("T.A <> 5")]
    [InlineData("T.A <= 5")]
    [InlineData("T.A >= 5")]
    [InlineData("T.A + 1 > 2")]
    [InlineData("T.A - 1 > 2")]
    [InlineData("T.A * 2 > 2")]
    [InlineData("T.A / 2 > 2")]
    [InlineData("T.A \\ 2 > 2")]
    [InlineData("T.A Mod 2 = 0")]
    [InlineData("T.A mod 2 = 0")]
    [InlineData("T.A ^ 2 > 1")]
    [InlineData("T.S & 'x' <> 'y'")]
    [InlineData("T.S + 'x' <> 'y'")]
    [InlineData("NOT T.A > 5")]
    [InlineData("not T.A > 5")]
    [InlineData("T.A > 0 OR T.A < -5")]
    [InlineData("T.A > 0 XOR T.A > 5")]
    [InlineData("T.A > 0 xor T.A > 1")]
    [InlineData("T.A > 0 EQV T.A > 5")]
    [InlineData("T.A > 0 IMP T.A > 5")]
    [InlineData("-T.A < 5")]
    [InlineData("T.A > 0 and T.A < 9 or T.A = 20")]
    [InlineData("T.A>0")]
    [InlineData("T.A > 0 = True")]
    [InlineData("NOT (T.A > 5)")]
    [InlineData("T.A > -(2)")]
    [InlineData("T.A + +1 > 0")]
    [InlineData("T.A IN (1, 2, 3)")]
    [InlineData("T.A In ( 1 , 2 )")]
    [InlineData("T.A in (1)")]
    [InlineData("T.A NOT IN (1, 2)")]
    [InlineData("T.A BETWEEN 1 AND 10")]
    [InlineData("T.A between 1 and 2")]
    [InlineData("T.A NOT BETWEEN 1 AND 10")]
    [InlineData("T.S LIKE 'a*'")]
    [InlineData("T.S like 'a*'")]
    [InlineData("T.S NOT LIKE 'a*'")]
    [InlineData("T.S ALIKE 'a%'")]
    [InlineData("T.A IS NOT NULL")]
    [InlineData("T.A is not null")]
    [InlineData("T.A IS NULL OR T.A > 0")]
    [InlineData("NOT T.A IS NULL")]
    [InlineData("T.A <> Null")]
    [InlineData("T.A = Null")]
    [InlineData("Null <> T.A")]
    // Re-formed: brackets.
    [InlineData("(T.A + 1) * 2 > 0")]
    [InlineData("T.A + (1 * 2) > 0")]
    [InlineData("((T.A > 0))")]
    [InlineData("(T.A > 0) AND (T.A < 9)")]
    [InlineData("T.A > 0 AND (T.A < 9 OR T.A = 20)")]
    [InlineData("T.A - (1 - 2) > 0")]
    [InlineData("T.A > 0 AND (T.A < 9 AND T.A > 1)")]
    [InlineData("(T.A > 0 OR T.A < 1) AND T.A > 2")]
    [InlineData("T.A * (2 * 3) > 0")]
    [InlineData("(T.A * 2) * 3 > 0")]
    [InlineData("-(T.A + 1) < 0")]
    [InlineData("NOT (T.A > 0 AND T.A < 9)")]
    [InlineData("(T.S & 'x') & 'y' <> ''")]
    [InlineData("T.S & ('x' & 'y') <> ''")]
    [InlineData("T.A ^ (2 ^ 3) > 0")]
    [InlineData("(T.A ^ 2) ^ 3 > 0")]
    [InlineData("(T.A > 0) = True")]
    [InlineData("T.A + -1 > 0")]
    [InlineData("T.A - -1 > 0")]
    [InlineData("T.A Between 1 + 1 And 10")]
    [InlineData("T.A In (1 + 1, 2)")]
    [InlineData("(T.A + 1) Mod 2 = 0")]
    [InlineData("T.A Mod (2 + 1) = 0")]
    [InlineData("T.A * 2 + 1 > 0")]
    [InlineData("T.A + 2 * 1 > 0")]
    [InlineData("(T.A + 2) & 'x' <> ''")]
    [InlineData("T.A & (2 + 1) <> ''")]
    [InlineData("(T.A \\ 2) * 3 > 0")]
    [InlineData("T.A \\ (2 * 3) > 0")]
    [InlineData("T.A / (2 * 3) > 0")]
    [InlineData("(T.A = 1) Or (T.A = 2) And (T.A = 3)")]
    [InlineData("((T.A = 1) Or (T.A = 2)) And (T.A = 3)")]
    [InlineData("NOT NOT T.A > 0")]
    [InlineData("-(-T.A) < 0")]
    [InlineData("(-T.A) ^ 2 > 0")]
    [InlineData("-T.A ^ 2 < 0")]
    [InlineData("(T.A > 0) Is Null")]
    [InlineData("(T.A Mod 2) * 3 = 0")]
    [InlineData("T.A > 0 Or T.A < 0 And T.A = 3")]
    [InlineData("(T.A > 0 Or T.A < 0) Xor T.A = 3")]
    [InlineData("T.A > 0 Eqv (T.A < 0 Imp T.A = 3)")]
    [InlineData("T.S Like ('a' & '*')")]
    // Re-formed: literals.
    [InlineData("T.S <> 'it''s'")]
    [InlineData("T.S <> \"dq\"")]
    [InlineData("T.S <> ''")]
    [InlineData("T.A > 1.50")]
    [InlineData("T.A > 1.0")]
    [InlineData("T.A > 1E3")]
    [InlineData("T.A > .5")]
    [InlineData("T.A > 007")]
    [InlineData("T.A > -1.5")]
    [InlineData("T.A > 1.5E-3")]
    [InlineData("T.A > 1E15")]
    [InlineData("T.A > 1E16")]
    [InlineData("T.A > 0.1")]
    [InlineData("T.A > 0.0001")]
    [InlineData("T.A > 0.00001")]
    [InlineData("T.A > 0")]
    [InlineData("T.A > 32768")]
    [InlineData("T.A > 2147483648")]
    [InlineData("T.A > 123456789012345")]
    [InlineData("T.A > 1.23456789012345678")]
    [InlineData("T.A > 100000000000000000000")]
    [InlineData("T.D > #2020-01-01#")]
    [InlineData("T.D > #2020-01-01 00:00:00#")]
    [InlineData("T.D > #1/2/2020 10:30:00 AM#")]
    [InlineData("T.D > #1/2/2020 3:04:05 PM#")]
    [InlineData("T.D > #10:30#")]
    [InlineData("T.D > #1/2/2020 10:05:07#")]
    [InlineData("T.D > #2020-01-31#")]
    [InlineData("T.D > #1899-12-30#")]
    [InlineData("T.D > #1/2/2020 0:00:01#")]
    [InlineData("T.D > #12:00:00 AM#")]
    [InlineData("T.D > #1/2/2020 12:00:00 PM#")]
    [InlineData("T.D > #100-01-01#")]
    [InlineData("T.B = True")]
    [InlineData("T.B = true")]
    [InlineData("T.B <> False")]
    [InlineData("T.B = Yes")]
    [InlineData("T.B = yes")]
    [InlineData("T.B = On")]
    [InlineData("T.B = no")]
    [InlineData("T.B = off")]
    [InlineData("IIf(T.A > 0, Null, 1) = 1")]
    [InlineData("T.A > 0 AND Null")]
    // Re-formed: functions and names.
    [InlineData("abs(T.A) < 100")]
    [InlineData("Abs ( T.A ) < 100")]
    [InlineData("IIF(T.A > 0, 1, 2) = 1")]
    [InlineData("iif(T.A > 0, 1, 2) = 1")]
    [InlineData("Mid(T.S, 1, 2) <> 'zz'")]
    [InlineData("T.D < Date()")]
    [InlineData("date() > T.D")]
    [InlineData("T.D < Now()")]
    [InlineData("NOW() > T.D")]
    [InlineData("T.D < Now")]
    [InlineData("Len( T.S ) > 0")]
    [InlineData("UCase(T.S) = 'X'")]
    [InlineData("DateAdd('d', 1, T.D) > #2020-01-01#")]
    [InlineData("T.A > 0 AND Format(T.D, \"yyyy\") > '2000'")]
    [InlineData("T.A > 0 AND [My Col] > 0")]
    [InlineData("t.a > 0")]
    [InlineData("T.A > 0 AND [A] < 5")]
    [InlineData("T.A > 0 AND `S` <> 'x'")]
    [InlineData("T.[My Col] > 0")]
    [InlineData("T.[A] > 0")]
    // Re-formed: subqueries, kept as written inside.
    [InlineData("T.A IN (SELECT EmployeeID FROM Employees)")]
    [InlineData("T.A NOT IN (SELECT EmployeeID FROM Employees)")]
    [InlineData("T.A > 0 AND EXISTS (SELECT * FROM Employees WHERE EmployeeID = T.A)")]
    [InlineData("T.A > (SELECT MIN(EmployeeID) FROM Employees)")]
    [InlineData("T.A > 0 AND NOT EXISTS (SELECT  *  FROM Employees WHERE EmployeeID = 999)")]
    [InlineData("T.A > 0 and exists (select * from Employees)")]
    [InlineData("Exists(SELECT * FROM Employees) And T.A > 0")]
    [InlineData("T.A > 0 And (SELECT COUNT(*) FROM Employees)>0")]
    [InlineData("T.A > 0 And A In(SELECT EmployeeID FROM Employees)")]
    [InlineData("T.A > 0 And A Not In(SELECT EmployeeID FROM Employees)")]
    [InlineData("T.A > 0 And Not Exists(SELECT * FROM Employees)")]
    [InlineData("T.A > 0 AND (SELECT COUNT(*) FROM Employees WHERE EmployeeID = T.A) > 0")]
    [InlineData("T.A > 0 AND (SELECT COUNT(*)   FROM T WHERE T.A > 5) < 1")]
    [InlineData("T.A IN (SELECT EmployeeID FROM Employees WHERE EmployeeID > 0 AND EmployeeID < 99)")]
    [InlineData("T.A > ANY (SELECT EmployeeID FROM Employees)")]
    [InlineData("T.A = SOME (SELECT EmployeeID FROM Employees)")]
    [InlineData("T.A <> ALL (SELECT EmployeeID FROM Employees)")]
    [InlineData("T.A > 0 And A >= All(SELECT EmployeeID FROM Employees)")]
    [InlineData("T.A < any (select EmployeeID from Employees)")]
    public void Libred_stores_or_refuses_a_check_as_ace_does(string check)
    {
        string sql = $"CREATE TABLE T (A LONG, S TEXT(50), D DATETIME, B BIT, [My Col] LONG, CONSTRAINT ck CHECK ({check}))";
        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string ace = TemporaryDatabase.CopyPath(northwind, "chktext-ace-");
        string libred = TemporaryDatabase.CopyPath(northwind, "chktext-lib-");
        try
        {
            string aceOutcome = Outcome(ace, () =>
            {
                using OleDbConnection connection = AceTestDatabase.Open(ace);
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            });
            string libredOutcome = Outcome(libred, () =>
            {
                using var db = JetDatabase.Open(libred, readOnly: false);
                new QueryEngine(db).ExecuteNonQuery(sql);
            });
            output.WriteLine($"ACE    {aceOutcome}\nLibRed {libredOutcome}");
            Assert.Equal(aceOutcome, libredOutcome);
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    /// <summary>The stored text, or the refusal's message.</summary>
    private static string Outcome(string path, Action create)
    {
        try
        {
            create();
        }
        catch (Exception e)
        {
            return $"refused: {e.Message}";
        }
        using var db = JetDatabase.Open(path);
        return $"stored «{db.Catalog.FindTable("T")!.CheckConstraints.Single().Expression}»";
    }
}
