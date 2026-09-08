using System.Diagnostics;
using LibRed.Engine;

namespace LibRed.Benchmarks.Harness;

/// <summary>
/// Runs every case in the corpus once and reports what it returned. Not a benchmark — a guard.
/// </summary>
/// <remarks>
/// A benchmark that throws is obvious. The failure that costs a day is the case that <i>succeeds</i> and
/// returns nothing, because a predicate was written against data the generator does not produce: it reports a
/// beautiful time for a query that does no work. So this prints the row count next to every case, and flags
/// an empty result as loudly as an exception, leaving the judgement to the reader — a few cases
/// (<c>predicate.is_null</c> aside) are supposed to return rows, and a zero there means the corpus and the
/// SQL have drifted apart.
/// <code>dotnet run -c Release -- --validate</code>
/// </remarks>
public static class CorpusValidator
{
    public static int Run(int scale)
    {
        Console.WriteLine($"Validating {SqlCorpus.All.Count()} cases at scale {scale:N0}\n");
        Console.WriteLine($"{"case",-32}{"rows",10}{"ms",10}  status");
        Console.WriteLine(new string('-', 72));

        int failures = 0;
        int empty = 0;

        foreach (IGrouping<CaseSource, SqlCase> bySource in SqlCorpus.All.GroupBy(c => c.Source))
        {
            using JetDatabase database = BenchmarkDatabase.Open(bySource.Key, scale);
            var engine = new QueryEngine(database);

            foreach (SqlCase sqlCase in bySource)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    long rows = CountRows(engine, sqlCase.Sql);
                    sw.Stop();
                    if (rows == 0) empty++;
                    Console.WriteLine(
                        $"{sqlCase.Name,-32}{rows,10:N0}{sw.Elapsed.TotalMilliseconds,10:F1}  {(rows == 0 ? "EMPTY" : "ok")}");
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    failures++;
                    Console.WriteLine($"{sqlCase.Name,-32}{"-",10}{sw.Elapsed.TotalMilliseconds,10:F1}  FAILED");
                    Console.WriteLine($"    {ex.GetType().Name}: {Single(ex.Message)}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{failures} failed, {empty} returned no rows.");
        return failures == 0 ? 0 : 1;
    }

    private static long CountRows(QueryEngine engine, string sql)
    {
        long rows = 0;
        foreach (object?[] row in engine.ExecuteQuery(sql).Rows)
        {
            _ = row.Length;
            rows++;
        }

        return rows;
    }

    /// <summary>Collapses a multi-line parser error onto one line so the report stays a table.</summary>
    private static string Single(string message) =>
        message.ReplaceLineEndings(" ").Trim();
}
