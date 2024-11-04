using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.TestUtilities.Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;

/// <summary>
/// Marks a test method or class that is known to crash the test runner.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class TestRunnerCrashAttribute(params AccessProviderTypeVariation[] accessProviderTypeVariations)
    : Attribute, ITestCondition
{
    public const string DefaultSkipReason = "The test is known to crash the test runner.";
    
    protected AccessProviderTypeVariation[] AccessProviderTypeVariations { get; } = accessProviderTypeVariations.Length > 0
        ? accessProviderTypeVariations
        : [AccessProviderTypeVariation.All];

    public virtual ValueTask<bool> IsMetAsync()
    {
        // Implement and enable if we want to filter tests by specific runtime scenarios.
        var currentVariation = AccessProviderTypeVariation.All; // AppConfig.AccessProviderTypeVariation;
        var isMet = AccessProviderTypeVariations.Any(v => v.HasFlag(currentVariation));

        if (!isMet && string.IsNullOrEmpty(Skip))
        {
            Skip = DefaultSkipReason;
        }

        return new ValueTask<bool>(isMet);
    }

    public virtual string SkipReason
        => Skip ?? DefaultSkipReason;

    public virtual string? Skip { get; set; }
}