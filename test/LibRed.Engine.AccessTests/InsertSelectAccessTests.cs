using System.Data.OleDb;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// The multiple-record append query, cross-checked against ACE. Tests that LibRed executes it are one thing;
// the question that decides whether the feature is right is what the real engine does with the same SQL.
//
// Two behaviours are worth measuring rather than assuming, because both are choices a reasonable
// implementation could make differently:
//   - appending a table to itself: does the source read complete before the write begins, or does the scan
//     consume its own output? Access is the authority on where that lands.
//   - a source that yields no rows: an error, or a no-op reporting zero?
[Collection(AceCollection.Name)]
public class InsertSelectAccessTests : TempDatabaseTest
{
    private static string Copy() => TemporaryDatabase.CopyPath(
        Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "insel-ace-");

    /// <summary>Runs the same statements through ACE and through LibRed, and compares what each ends up with.</summary>
    private static void AssertSameAsAce(string[] setup, string append, string verify)
    {
        string acePath = Copy(), libRedPath = Copy();
        try
        {
            object?[] aceRows;
            using (var connection = AceTestDatabase.Open(acePath))
            {
                foreach (string sql in setup) Exec(connection, sql);
                Exec(connection, append);
                using var command = connection.CreateCommand();
                command.CommandText = verify;
                using var reader = command.ExecuteReader();
                var rows = new List<object?>();
                while (reader.Read()) rows.Add(reader.GetValue(0));
                aceRows = [.. rows];
            }

            using var db = TemporaryDatabase.OpenTracked(libRedPath, readOnly: false);
            var engine = new QueryEngine(db);
            foreach (string sql in setup) engine.ExecuteNonQuery(sql);
            engine.ExecuteNonQuery(append);
            object?[] ourRows = [.. engine.ExecuteQuery(verify).Rows.Select(r => r[0])];

            Assert.Equal(
                aceRows.Select(v => Convert.ToString(v)),
                ourRows.Select(v => Convert.ToString(v)));
        }
        finally
        {
            TemporaryDatabase.Delete(acePath);
            TemporaryDatabase.Delete(libRedPath);
        }
    }

    [Fact]
    public void Appends_the_sources_rows_as_ACE_does() => AssertSameAsAce(
        [
            "CREATE TABLE SrcA (Id LONG, Name TEXT(50))",
            "CREATE TABLE DstA (Id LONG, Name TEXT(50))",
            "INSERT INTO SrcA (Id, Name) VALUES (1, 'one')",
            "INSERT INTO SrcA (Id, Name) VALUES (2, 'two')",
            "INSERT INTO SrcA (Id, Name) VALUES (3, 'three')",
        ],
        "INSERT INTO DstA (Id, Name) SELECT Id, Name FROM SrcA WHERE Id > 1",
        "SELECT Name FROM DstA ORDER BY Id");

    // Without a column list the source's output NAMES choose the target columns. The aliases are REVERSED
    // here, so the values arrive in the opposite order to the names — the one arrangement where name-based
    // and positional resolution give different answers instead of coinciding. ACE stores Id=7; positionally
    // it would have stored 'seven' there.
    [Fact]
    public void Resolves_by_name_without_a_column_list_as_ACE_does() => AssertSameAsAce(
        [
            "CREATE TABLE SrcB (A LONG, B TEXT(50))",
            "CREATE TABLE DstB (Id LONG, Name TEXT(50))",
            "INSERT INTO SrcB (A, B) VALUES (7, 'seven')",
            "INSERT INTO SrcB (A, B) VALUES (8, 'eight')",
        ],
        "INSERT INTO DstB SELECT B AS Name, A AS Id FROM SrcB",
        "SELECT Name FROM DstB ORDER BY Id");

    // The Halloween case. If the scan fed its own output back in, this would not terminate at all — so the
    // test hanging IS the failure, and agreeing with ACE on the row count is the pass.
    [Fact]
    public void Appending_a_table_to_itself_matches_ACE() => AssertSameAsAce(
        [
            "CREATE TABLE SelfC (Id LONG, Name TEXT(50))",
            "INSERT INTO SelfC (Id, Name) VALUES (1, 'a')",
            "INSERT INTO SelfC (Id, Name) VALUES (2, 'b')",
        ],
        "INSERT INTO SelfC (Id, Name) SELECT Id, Name FROM SelfC",
        "SELECT COUNT(*) FROM SelfC");

    [Fact]
    public void An_empty_source_appends_nothing_as_ACE_does() => AssertSameAsAce(
        [
            "CREATE TABLE SrcD (Id LONG)",
            "CREATE TABLE DstD (Id LONG)",
        ],
        "INSERT INTO DstD (Id) SELECT Id FROM SrcD WHERE Id > 0",
        "SELECT COUNT(*) FROM DstD");

    // No cross-check for column DEFAULTs: ACE's DDL over OLE DB rejects `DEFAULT 'x'` in CREATE TABLE with
    // "Syntax error in field definition" — a default is a column property Access sets through DAO/ADOX, not
    // something its SQL DDL can express. So the setup for such a comparison cannot be written on the ACE
    // side at all, and the behaviour is covered by InsertSelectTests on the LibRed side instead.
    //
    // Worth recording rather than silently omitting: it is a limit of what can be COMPARED, not a place the
    // two engines were found to differ.

    private static void Exec(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
