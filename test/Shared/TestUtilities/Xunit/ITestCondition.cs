using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

/// <summary>
/// A condition that decides whether the tests it is applied to should run, and says why when they should not.
/// </summary>
/// <remarks>
/// EF Core 11 removed its own <c>ITestCondition</c> along with the xunit v2 conditional infrastructure that
/// evaluated it. Per-method and per-class conditions moved to xunit v3's native skipping — <c>SkipWhen</c> /
/// <c>SkipUnless</c> on a fact, and <c>ConditionalClass</c> for a whole class. Neither covers an
/// <b>assembly-level</b> condition, which is the one case still using this: the providers each apply one to
/// declare that nothing can run without a configured test database.
/// </remarks>
public interface ITestCondition
{
    /// <summary>Whether the condition is satisfied, so the tests it guards may run.</summary>
    ValueTask<bool> IsMetAsync();

    /// <summary>Why the tests were skipped, reported when <see cref="IsMetAsync" /> is false.</summary>
    string SkipReason { get; }
}
