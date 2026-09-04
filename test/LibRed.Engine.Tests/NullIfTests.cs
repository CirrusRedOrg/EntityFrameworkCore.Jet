using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// NULLIF(a, b) — NULL when the two are equal, otherwise a.
//
// A deliberate divergence from ACE, which has no such function ("Undefined function 'NULLIF' in expression",
// verified). Access spells it IIF(a = b, NULL, a). LibRed accepts the name EF Core emits so those queries run
// here; the results must match the IIF form exactly, including the NULL cases.
public class NullIfTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "nullif-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE `NI` (`Id` LONG NOT NULL PRIMARY KEY, `A` LONG, `B` LONG, `S` TEXT(20))");
        engine.ExecuteNonQuery("INSERT INTO `NI` (`Id`, `A`, `B`, `S`) VALUES (1, 5, 5, 'x')");
        engine.ExecuteNonQuery("INSERT INTO `NI` (`Id`, `A`, `B`, `S`) VALUES (2, 5, 7, 'y')");
        engine.ExecuteNonQuery("INSERT INTO `NI` (`Id`, `A`, `B`, `S`) VALUES (3, NULL, 7, NULL)");
        engine.ExecuteNonQuery("INSERT INTO `NI` (`Id`, `A`, `B`, `S`) VALUES (4, 5, NULL, 'z')");
        return engine;
    }

    private static object? Eval(QueryEngine engine, string projection, int id) =>
        engine.ExecuteQuery($"SELECT {projection} FROM `NI` WHERE `Id` = {id}").Rows.Single()[0];

    [Fact]
    public void Equal_values_give_null()
        => Assert.Null(Eval(Seeded(), "NULLIF(`A`, `B`)", 1));

    [Fact]
    public void Differing_values_give_the_first()
        => Assert.Equal(5, Convert.ToInt32(Eval(Seeded(), "NULLIF(`A`, `B`)", 2)));

    // Comparing with NULL is unknown, not equal, so the first operand comes back — which for a NULL first
    // operand is itself NULL. Both match the IIF spelling, where an unknown condition takes the false branch.
    [Fact]
    public void A_null_first_operand_gives_null()
        => Assert.Null(Eval(Seeded(), "NULLIF(`A`, `B`)", 3));

    [Fact]
    public void A_null_second_operand_gives_the_first()
        => Assert.Equal(5, Convert.ToInt32(Eval(Seeded(), "NULLIF(`A`, `B`)", 4)));

    [Fact]
    public void Works_on_text()
    {
        QueryEngine engine = Seeded();
        Assert.Null(Eval(engine, "NULLIF(`S`, 'x')", 1));
        Assert.Equal("y", Eval(engine, "NULLIF(`S`, 'x')", 2));
    }

    [Fact]
    public void Works_on_literals()
    {
        QueryEngine engine = Seeded();
        Assert.Null(Eval(engine, "NULLIF(1, 1)", 1));
        Assert.Equal(1, Convert.ToInt32(Eval(engine, "NULLIF(1, 2)", 1)));
    }

    // The results must equal Access's own spelling of the same thing, on every row.
    [Fact]
    public void Matches_the_IIF_spelling_on_every_row()
    {
        QueryEngine engine = Seeded();
        var rows = engine.ExecuteQuery(
            "SELECT NULLIF(`A`, `B`) AS N, IIF(`A` = `B`, NULL, `A`) AS I FROM `NI` ORDER BY `Id`").Rows.ToList();

        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.Equal(r[1], r[0]));
    }

    // Arity is enforced like every other function's.
    [Theory]
    [InlineData("NULLIF(`A`)")]
    [InlineData("NULLIF(`A`, `B`, 1)")]
    public void Wrong_argument_count_is_rejected(string projection)
        => Assert.ThrowsAny<Exception>(() => Eval(Seeded(), projection, 1));

    // "Returns the same type as the first expression" — NULLIF yields either that expression or a NULL of its
    // type, so unlike COALESCE it does not unify across both arguments. The second one only ever takes part in
    // the comparison, and a differing type there must not change what the column declares.
    private static Type ColumnType(QueryEngine engine, string projection)
        => engine.ExecuteQuery($"SELECT {projection} FROM `NI` WHERE `Id` = 1").ColumnTypes[0];

    [Fact]
    public void Declares_the_type_of_its_first_argument()
        => Assert.Equal(typeof(int), ColumnType(Seeded(), "NULLIF(`A`, `B`)"));

    [Fact]
    public void Declares_the_first_argument_type_even_when_the_second_differs()
        => Assert.Equal(typeof(string), ColumnType(Seeded(), "NULLIF(`S`, 1)"));
}
