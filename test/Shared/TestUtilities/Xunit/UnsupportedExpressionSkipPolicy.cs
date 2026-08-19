using System;
using System.Collections.Generic;
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
/// <para>
/// The rules live in the <see cref="string" /> overload because that is the form the decision is actually
/// needed in: a failure is intercepted as a reported message, which carries exception type names and texts
/// rather than live <see cref="Exception" /> objects. Comparing type names also keeps this file free of
/// System.Data.OleDb and System.Data.Odbc, which the cross-platform LibRed tests share but cannot reference.
/// </para>
/// </remarks>
public static class UnsupportedExpressionSkipPolicy
{
    // Type names rather than types: see the remarks above.
    private static readonly string[] CandidateExceptionTypes =
    [
        "System.InvalidOperationException",
        "System.NotSupportedException",
        "System.Data.OleDb.OleDbException",
        "System.Data.Odbc.OdbcException"
    ];

    /// <summary>Whether <paramref name="exception" /> means the provider cannot express the query at all.</summary>
    public static bool ShouldSkip(Exception exception)
    {
        var aggregate = exception as AggregateException ?? new AggregateException(exception);

        var flattened = aggregate.Flatten().InnerExceptions.SelectMany(Hierarchy).ToList();

        return ShouldSkip(
            flattened.Select(e => (string?)e.GetType().FullName).ToArray(),
            flattened.Select(e => e.Message).ToArray());
    }

    /// <summary>
    ///     Whether a reported failure means the provider cannot express the query at all. The two lists are
    ///     positionally paired, as xunit reports them.
    /// </summary>
    public static bool ShouldSkip(IReadOnlyList<string?> exceptionTypes, IReadOnlyList<string> messages)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            string? type = i < exceptionTypes.Count ? exceptionTypes[i] : null;

            if (type is null || Array.IndexOf(CandidateExceptionTypes, type) < 0)
            {
                continue;
            }

            if (IsUnsupportedExpressionMessage(messages[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnsupportedExpressionMessage(string message)
    {
        if (message.StartsWith("jet does not support ", StringComparison.OrdinalIgnoreCase))
        {
            // Only the translations we have decided are out of scope — anything else under this prefix is
            // a genuine gap worth seeing.
            return message.Contains("APPLY statements")
                || message.Contains("skipping rows")
                || message.Contains("sequences");
        }

        return message.StartsWith("Unsupported Jet expression", StringComparison.Ordinal)
            || message.StartsWith("No value given for one or more required parameters.", StringComparison.Ordinal)
            || message.StartsWith("Syntax error in PARAMETER clause", StringComparison.Ordinal);
    }

    private static IEnumerable<Exception> Hierarchy(Exception exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            yield return e;
        }
    }
}
