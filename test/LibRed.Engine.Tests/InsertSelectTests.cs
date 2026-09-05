using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Access's MULTIPLE-RECORD append query:
//   INSERT INTO target [(field, …)] SELECT [source.]field, … FROM tableexpression
//
// The single-record form is the VALUES one. Access has no multi-row VALUES syntax at all, so "many rows in
// one INSERT" and "rows from a query" are the same feature there, not two.
//
// The IN externaldatabase clause both forms allow is not implemented: appending into another file belongs to
// the linked-database subsystem LibRed does not have.
public class InsertSelectTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "insel-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    [Fact]
    public void Appends_every_row_the_source_produces()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE Src (Id LONG, Name TEXT(50))");
        engine.ExecuteNonQuery("CREATE TABLE Dst (Id LONG, Name TEXT(50))");
        engine.ExecuteNonQuery("INSERT INTO Src (Id, Name) VALUES (1, 'one')");
        engine.ExecuteNonQuery("INSERT INTO Src (Id, Name) VALUES (2, 'two')");
        engine.ExecuteNonQuery("INSERT INTO Src (Id, Name) VALUES (3, 'three')");

        int affected = engine.ExecuteNonQuery("INSERT INTO Dst (Id, Name) SELECT Id, Name FROM Src");

        Assert.Equal(3, affected);
        Assert.Equal(3, Convert.ToInt32(engine.ExecuteQuery("SELECT COUNT(*) FROM Dst").Rows.Single()[0]));
        Assert.Equal("two", engine.ExecuteQuery("SELECT Name FROM Dst WHERE Id = 2").Rows.Single()[0]);
    }

    [Fact]
    public void Applies_the_sources_where_and_order()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE Src (Id LONG, Name TEXT(50))");
        engine.ExecuteNonQuery("CREATE TABLE Dst (Id LONG, Name TEXT(50))");
        for (int i = 1; i <= 5; i++)
            engine.ExecuteNonQuery($"INSERT INTO Src (Id, Name) VALUES ({i}, 'n{i}')");

        int affected = engine.ExecuteNonQuery("INSERT INTO Dst (Id, Name) SELECT Id, Name FROM Src WHERE Id > 3");

        Assert.Equal(2, affected);
        Assert.Equal([4, 5], engine.ExecuteQuery("SELECT Id FROM Dst ORDER BY Id").Rows.Select(r => Convert.ToInt32(r[0])));
    }

    // No column list: the source's output NAMES choose the target columns. Measured against ACE — it is not
    // positional, and the difference is silent when the columns are type-compatible.
    [Fact]
    public void Without_a_column_list_the_source_names_choose_the_columns()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE Src (A LONG, B TEXT(50))");
        engine.ExecuteNonQuery("CREATE TABLE Dst (Id LONG, Name TEXT(50))");
        engine.ExecuteNonQuery("INSERT INTO Src (A, B) VALUES (7, 'seven')");

        engine.ExecuteNonQuery("INSERT INTO Dst SELECT A AS Id, B AS Name FROM Src");

        object?[] row = engine.ExecuteQuery("SELECT Id, Name FROM Dst").Rows.Single();
        Assert.Equal(7, Convert.ToInt32(row[0]));
        Assert.Equal("seven", row[1]);
    }

    // The case where name-based and positional resolution actually disagree: the aliases are reversed, so the
    // values arrive in the opposite order to the names. ACE routes by name, storing Id=7 — positionally it
    // would have put 'seven' there.
    [Fact]
    public void Reversed_aliases_route_by_name_not_position()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE Src (A LONG, B TEXT(50))");
        engine.ExecuteNonQuery("CREATE TABLE Dst (Id LONG, Name TEXT(50))");
        engine.ExecuteNonQuery("INSERT INTO Src (A, B) VALUES (7, 'seven')");

        engine.ExecuteNonQuery("INSERT INTO Dst SELECT B AS Name, A AS Id FROM Src");

        object?[] row = engine.ExecuteQuery("SELECT Id, Name FROM Dst").Rows.Single();
        Assert.Equal(7, Convert.ToInt32(row[0]));
        Assert.Equal("seven", row[1]);
    }

    // A source name the target does not have is an error, as it is in ACE ("unknown field name: 'A'").
    [Fact]
    public void A_source_name_the_target_lacks_is_rejected()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE Src (A LONG, B TEXT(50))");
        engine.ExecuteNonQuery("CREATE TABLE Dst (Id LONG, Name TEXT(50))");
        engine.ExecuteNonQuery("INSERT INTO Src (A, B) VALUES (7, 'seven')");

        Assert.Throws<InvalidOperationException>(
            () => engine.ExecuteNonQuery("INSERT INTO Dst SELECT A, B FROM Src"));
    }

    // The Halloween problem: appending a table to itself must read the source to completion BEFORE writing,
    // or the scan consumes its own output and never terminates. Access doubles the table and stops.
    [Fact]
    public void Appending_a_table_to_itself_doubles_it_and_terminates()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE Self (Id LONG, Name TEXT(50))");
        engine.ExecuteNonQuery("INSERT INTO Self (Id, Name) VALUES (1, 'a')");
        engine.ExecuteNonQuery("INSERT INTO Self (Id, Name) VALUES (2, 'b')");

        int affected = engine.ExecuteNonQuery("INSERT INTO Self (Id, Name) SELECT Id, Name FROM Self");

        Assert.Equal(2, affected);
        Assert.Equal(4, Convert.ToInt32(engine.ExecuteQuery("SELECT COUNT(*) FROM Self").Rows.Single()[0]));
    }

    // Columns the append does not mention still take their DEFAULT, exactly as in the VALUES form — the two
    // forms share that path rather than each having their own.
    [Fact]
    public void Unmentioned_columns_take_their_default()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE Src (Id LONG)");
        engine.ExecuteNonQuery("CREATE TABLE Dst (Id LONG, Note TEXT(50) DEFAULT 'unset')");
        engine.ExecuteNonQuery("INSERT INTO Src (Id) VALUES (1)");

        engine.ExecuteNonQuery("INSERT INTO Dst (Id) SELECT Id FROM Src");

        Assert.Equal("unset", engine.ExecuteQuery("SELECT Note FROM Dst").Rows.Single()[0]);
    }

    // An AutoNumber target generates its own ids rather than taking the source's, and @@IDENTITY reports the
    // LAST one — the whole point of a multi-row append being one statement.
    [Fact]
    public void An_autonumber_target_generates_ids_and_publishes_the_last()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE Src (Name TEXT(50))");
        engine.ExecuteNonQuery("CREATE TABLE Dst (Id COUNTER PRIMARY KEY, Name TEXT(50))");
        engine.ExecuteNonQuery("INSERT INTO Src (Name) VALUES ('a')");
        engine.ExecuteNonQuery("INSERT INTO Src (Name) VALUES ('b')");

        engine.ExecuteNonQuery("INSERT INTO Dst (Name) SELECT Name FROM Src");

        Assert.Equal([1, 2], engine.ExecuteQuery("SELECT Id FROM Dst ORDER BY Id").Rows.Select(r => Convert.ToInt32(r[0])));
        Assert.Equal(2, Convert.ToInt32(engine.ExecuteQuery("SELECT @@IDENTITY").Rows.Single()[0]));
    }

    // A UNION feeding an append. Access documents the source as a SELECT; this is the shape EF emits from a
    // Concat, and costs nothing extra because the source is planned as any query expression.
    [Fact]
    public void A_union_can_feed_an_append()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE A (Id LONG)");
        engine.ExecuteNonQuery("CREATE TABLE B (Id LONG)");
        engine.ExecuteNonQuery("CREATE TABLE Dst (Id LONG)");
        engine.ExecuteNonQuery("INSERT INTO A (Id) VALUES (1)");
        engine.ExecuteNonQuery("INSERT INTO B (Id) VALUES (2)");

        int affected = engine.ExecuteNonQuery("INSERT INTO Dst (Id) SELECT Id FROM A UNION ALL SELECT Id FROM B");

        Assert.Equal(2, affected);
        Assert.Equal([1, 2], engine.ExecuteQuery("SELECT Id FROM Dst ORDER BY Id").Rows.Select(r => Convert.ToInt32(r[0])));
    }

    [Fact]
    public void A_mismatched_column_count_is_rejected()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE Src (Id LONG, Name TEXT(50))");
        engine.ExecuteNonQuery("CREATE TABLE Dst (Id LONG, Name TEXT(50))");
        engine.ExecuteNonQuery("INSERT INTO Src (Id, Name) VALUES (1, 'one')");

        Assert.Throws<InvalidOperationException>(
            () => engine.ExecuteNonQuery("INSERT INTO Dst (Id, Name) SELECT Id FROM Src"));
    }

    // A source that yields nothing is not an error: zero rows appended, and @@ROWCOUNT says so.
    [Fact]
    public void An_empty_source_appends_nothing()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE Src (Id LONG)");
        engine.ExecuteNonQuery("CREATE TABLE Dst (Id LONG)");

        int affected = engine.ExecuteNonQuery("INSERT INTO Dst (Id) SELECT Id FROM Src WHERE Id > 0");

        Assert.Equal(0, affected);
        Assert.Equal(0, Convert.ToInt32(engine.ExecuteQuery("SELECT COUNT(*) FROM Dst").Rows.Single()[0]));
    }
}
