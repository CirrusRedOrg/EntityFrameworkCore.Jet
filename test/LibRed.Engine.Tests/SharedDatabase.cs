using System.Globalization;
using LibRed.Engine;

namespace LibRed.Engine.Tests;

/// <summary>
/// One Northwind copy for a whole test class, set up once, for classes whose tests only read — the function and
/// operator tables, where every case is a single SELECT. A class takes it as an xunit class fixture through a nested
/// sealed subclass that supplies its setup; xunit disposes it, closing and deleting the copy, when the class is done.
/// A test that writes uses <see cref="Fresh"/> for a copy of its own instead.
/// </summary>
public abstract class SharedDatabase : IDisposable
{
    private static string Northwind => Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    private readonly TemporaryDatabase _copy;

    protected SharedDatabase(string prefix, IReadOnlyList<string> setup)
    {
        _copy = TemporaryDatabase.CopyOf(Northwind, prefix);
        Engine = Prepare(new QueryEngine(_copy.Open()), setup);
    }

    public QueryEngine Engine { get; }

    /// <summary>A copy of its own with the same setup, released when the calling test ends.</summary>
    public static QueryEngine Fresh(string prefix, IReadOnlyList<string> setup) =>
        Prepare(new QueryEngine(TemporaryDatabase.OpenTracked(TemporaryDatabase.CopyPath(Northwind, prefix))), setup);

    /// <summary>The first value of the query's first row, run under <paramref name="culture"/>. The evaluator reads and
    /// writes text in the regional format, so a test pins the culture whatever the machine's is.</summary>
    public object? Scalar(string sql, CultureInfo culture) => Scalar(Engine, sql, culture);

    /// <inheritdoc cref="Scalar(string, CultureInfo)"/>
    public static object? Scalar(QueryEngine engine, string sql, CultureInfo culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return engine.ExecuteQuery(sql).Rows.First()[0];
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static QueryEngine Prepare(QueryEngine engine, IReadOnlyList<string> setup)
    {
        foreach (string statement in setup)
            engine.ExecuteNonQuery(statement);
        return engine;
    }

    public void Dispose()
    {
        _copy.Dispose();
        GC.SuppressFinalize(this);
    }
}
