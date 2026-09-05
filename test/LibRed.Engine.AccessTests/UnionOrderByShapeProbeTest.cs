using System.Data.OleDb;
using Xunit;

namespace LibRed.Engine.Tests;

// PROBE: what `SELECT … UNION SELECT … ORDER BY x` means, and whether LibRed agrees.
//
// LibRed's grammar is `queryExpression : queryTerm (setOperator queryTerm)*` with ORDER BY living on
// selectStatement, so a trailing ORDER BY binds to the LAST OPERAND rather than to the whole union. The
// standard attaches it to the query expression as a whole, and Access supports the construct, so LibRed is
// likely producing a wrongly-ordered result rather than an error — the worst failure mode. This settles it
// against ACE rather than by reading the grammar, and answers three things at once:
//   1. does ACE accept it, and in what order does it return the rows;
//   2. does ACE accept ORDER BY on a NON-final operand, which is what LibRed's parse actually means;
//   3. what LibRed itself returns for the same text.
// The data is chosen so "ordered as a whole" and "only the last operand ordered" give visibly different
// answers: the arms interleave, and neither is stored in sorted order.
[Collection(AceCollection.Name)]
public class UnionOrderByShapeProbeTest(ITestOutputHelper output) : TempDatabaseTest
{
    [Fact]
    public void Probe_union_order_by_binding()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_UNION_ORDERBY") == "1",
            "set LIBRED_UNION_ORDERBY=1 — this probe needs ACE");

        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "union-ob-probe-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE UA (V LONG)");
                Exec(connection, "CREATE TABLE UB (V LONG)");
                Exec(connection, "INSERT INTO UA (V) VALUES (50)");
                Exec(connection, "INSERT INTO UA (V) VALUES (10)");
                Exec(connection, "INSERT INTO UB (V) VALUES (40)");
                Exec(connection, "INSERT INTO UB (V) VALUES (20)");

                // Ordered as a whole gives 10,20,40,50. Last-operand-only gives UA in storage order (50,10)
                // followed by UB sorted (20,40).
                foreach ((string label, string sql) in Cases("", ""))
                    Report("ACE   ", label, () => AceRows(connection, sql));
            }

            output.WriteLine("");
            var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: true));
            foreach ((string label, string sql) in Cases("`", "`"))
                Report("LibRed", label, () => string.Join(",", engine.ExecuteQuery(sql).Rows.Select(r => r[0])));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static (string Label, string Sql)[] Cases(string o, string c) =>
    [
        ("trailing ORDER BY",      $"SELECT {o}V{c} FROM {o}UA{c} UNION ALL SELECT {o}V{c} FROM {o}UB{c} ORDER BY {o}V{c}"),
        ("trailing, DESC",         $"SELECT {o}V{c} FROM {o}UA{c} UNION ALL SELECT {o}V{c} FROM {o}UB{c} ORDER BY {o}V{c} DESC"),
        ("UNION (dedupe) + ORDER", $"SELECT {o}V{c} FROM {o}UA{c} UNION SELECT {o}V{c} FROM {o}UB{c} ORDER BY {o}V{c}"),
        // If ACE rejects this, ORDER BY provably belongs to the whole expression rather than to an operand.
        ("ORDER BY on first arm",  $"SELECT {o}V{c} FROM {o}UA{c} ORDER BY {o}V{c} UNION ALL SELECT {o}V{c} FROM {o}UB{c}"),
        // The unambiguous spelling, for reference — ordering a derived table over the union.
        ("wrapped in a subquery",  $"SELECT {o}V{c} FROM (SELECT {o}V{c} FROM {o}UA{c} UNION ALL SELECT {o}V{c} FROM {o}UB{c}) AS {o}U{c} ORDER BY {o}V{c}"),
    ];

    private void Report(string who, string label, Func<string> run)
    {
        try { output.WriteLine($"  {who}  {label,-24} {run()}"); }
        catch (Exception e) { output.WriteLine($"  {who}  {label,-24} rejected — {e.Message.Split('.')[0]}"); }
    }

    private static string AceRows(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
            values.Add(reader.GetValue(0).ToString() ?? "");
        return string.Join(",", values);
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
