using System;
using System.Linq;
using System.Reflection;
using Xunit;
using Xunit.v3;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

/// <summary>
/// Evaluates the <see cref="ITestCondition" />s applied to the test assembly, skipping every test with the
/// condition's own reason when one is not met.
/// </summary>
/// <remarks>
/// <para>
/// The providers each apply one such condition to declare that nothing can run without a configured test
/// database, so that an unconfigured machine reports thousands of honest skips rather than thousands of
/// failures.
/// </para>
/// <para>
/// EF Core 11 removed the xunit v2 machinery that used to evaluate these. Per-method and per-class
/// conditions moved to v3's native skipping, but an assembly-level one has no native equivalent, so it is
/// evaluated here instead. Reading the conditions off the entry assembly is exact under v3, where the test
/// project is itself the executable.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class TestConditionsAttribute : BeforeAfterTestAttribute
{
    // Evaluated once: the conditions describe the environment, which does not change mid-run.
    private static readonly Lazy<string?> SkipReason = new(Evaluate);

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (SkipReason.Value is { } reason)
        {
            Assert.Skip(reason);
        }
    }

    private static string? Evaluate()
    {
        var assembly = Assembly.GetEntryAssembly();

        if (assembly is null)
        {
            return null;
        }

        foreach (var condition in assembly.GetCustomAttributes().OfType<ITestCondition>())
        {
            // Synchronous by necessity — the hook this runs from is synchronous. The conditions are cheap
            // environment checks, and this happens once for the whole assembly.
            if (!condition.IsMetAsync().AsTask().GetAwaiter().GetResult())
            {
                return condition.SkipReason;
            }
        }

        return null;
    }
}
