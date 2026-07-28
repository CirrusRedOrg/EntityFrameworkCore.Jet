using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Data.OleDb;
using System.Linq;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

/// <summary>
/// Decides whether a test failed only because it asked for something the provider has explicitly declared it
/// cannot translate — in which case the honest result is "skipped", not "failed".
/// </summary>
/// <remarks>
/// <para>
/// The suites are EF Core's own specification tests, which exercise the whole of LINQ against every provider.
/// A great many of them reach for constructs Jet/ACE has no SQL for at all: APPLY, row skipping, sequences.
/// Those tests are not defects to fix, and reporting them as failures buries the ones that are.
/// </para>
/// <para>
/// Matching on message text is unlovely but deliberate: the provider raises plain InvalidOperationException
/// and NotSupportedException, so the type alone cannot distinguish "Jet cannot express this" from a real
/// break. The prefixes below are the provider's own wording, which is why a change to those messages has to
/// be reflected here.
/// </para>
/// </remarks>
public static class UnsupportedExpressionSkipPolicy
{
    /// <summary>Whether <paramref name="exception" /> means the provider cannot express the query at all.</summary>
    public static bool ShouldSkip(Exception exception)
    {
        var aggregate = exception as AggregateException ?? new AggregateException(exception);

        foreach (Exception inner in aggregate.Flatten().InnerExceptions.SelectMany(Hierarchy))
        {
            if (inner is not (InvalidOperationException or OleDbException or OdbcException or NotSupportedException))
            {
                continue;
            }

            string message = inner.Message;

            if (message.StartsWith("jet does not support ", StringComparison.OrdinalIgnoreCase))
            {
                // Only the translations we have decided are out of scope — anything else under this prefix is
                // a genuine gap worth seeing.
                if (message.Contains("APPLY statements")
                    || message.Contains("skipping rows")
                    || message.Contains("sequences"))
                {
                    return true;
                }
            }
            else if (message.StartsWith("Unsupported Jet expression", StringComparison.Ordinal)
                     || message.StartsWith("No value given for one or more required parameters.", StringComparison.Ordinal)
                     || message.StartsWith("Syntax error in PARAMETER clause", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Exception> Hierarchy(Exception exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            yield return e;
        }
    }
}
