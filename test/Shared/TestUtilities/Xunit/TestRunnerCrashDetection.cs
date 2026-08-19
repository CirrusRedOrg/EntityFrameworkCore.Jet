using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using Xunit;
using Xunit.v3;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

/// <summary>
/// Survives a test that takes the whole test runner down with it — an access violation out of the ACE engine,
/// say — by recording which test was in flight, so the next run can skip it instead of dying the same way.
/// </summary>
/// <remarks>
/// <para>
/// The protocol is deliberately file-based, because it has to survive a process that never gets to run any
/// more of its own code. Before each test a marker naming it is written; after the test the marker is
/// deleted. A marker still present at startup can only mean the process died while that test was running, so
/// <see cref="PromoteCrashesOfPreviousRuns" /> appends it to <see cref="TestsKnownToCrashTestRunnerFilePath" />
/// and, from then on, <see cref="Before" /> skips it.
/// </para>
/// <para>
/// Applied at assembly level, so it covers every test without per-test annotation.
/// </para>
/// <para>
/// This replaces four xunit v2 classes — a test framework, a framework discoverer and two test-case runners —
/// which existed only to reach a point either side of the test body. v3 offers that directly through
/// <see cref="BeforeAfterTestAttribute" />, so none of the surrounding machinery has to be reproduced, and no
/// custom test framework is needed at all.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TestRunnerCrashDetectionAttribute : BeforeAfterTestAttribute
{
    public const string CrashCacheDirectory = "TestRunnerCrashCache";
    public const string TestsKnownToCrashTestRunnerFilePath = "./../../../TestsKnownToCrashTestRunner.txt";
    public const string AutoSkipPrefix = "[AutoSkip]";

    public const string AutoSkipEnvironmentVariableName = "EFCoreJet_AutoSkipTestRunnerCrashingTests";
    public const string DetectCrashesEnvironmentVariableName = "EFCoreJet_DetectCrashesOfPreviousRuns";

    // The marker path per in-flight test. Keyed by the test itself, since tests run concurrently and each
    // needs to delete its own marker rather than the most recently written one.
    private static readonly ConcurrentDictionary<IXunitTest, string> InFlight = new();

    private static readonly Lazy<string[]> KnownCrashers = new(ReadKnownCrashers);

    private static bool AutoSkipEnabled
        => (Environment.GetEnvironmentVariable(AutoSkipEnvironmentVariableName)?.ToLowerInvariant() ?? "true") != "false";

    /// <summary>
    ///     Promotes markers left behind by a process that died mid-test into the known-crashers list. Call once
    ///     per run, before any test starts.
    /// </summary>
    public static void PromoteCrashesOfPreviousRuns()
    {
        if (Environment.GetEnvironmentVariable(DetectCrashesEnvironmentVariableName)?.ToLowerInvariant() != "true"
            || !Directory.Exists(CrashCacheDirectory))
        {
            return;
        }

        foreach (string filePath in Directory.EnumerateFiles(CrashCacheDirectory, "*.txt"))
        {
            // The file name carries when and on which architecture it happened; the contents name the test.
            string contents = $"{string.Join('\t', Path.GetFileNameWithoutExtension(filePath).Split('_'))}\t{File.ReadAllText(filePath)}\n";
            File.AppendAllText(TestsKnownToCrashTestRunnerFilePath, contents);
            File.Delete(filePath);
        }
    }

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        string testClass = test.TestCase.TestClassName ?? string.Empty;
        string testMethod = test.TestCase.TestMethodName ?? string.Empty;

        if (AutoSkipEnabled && IsKnownToCrash(testClass, testMethod))
        {
            // Skipping rather than running is the whole point: this test has already taken a runner down.
            Assert.Skip($"{AutoSkipPrefix} {TestRunnerCrashAttribute.DefaultSkipReason}");
        }

        Directory.CreateDirectory(CrashCacheDirectory);

        string marker = Path.Combine(
            CrashCacheDirectory,
            $"{DateTime.UtcNow:yyyyMMdd'_'HHmmss.fffffff}_{(Environment.Is64BitProcess ? "x64" : "x86")}_{Guid.NewGuid()}.txt");

        File.WriteAllText(marker, $"{testClass}\t{testMethod}");
        InFlight[test] = marker;
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        // The test finished, however it finished — so it did not take the process with it.
        if (InFlight.TryRemove(test, out string? marker) && File.Exists(marker))
        {
            File.Delete(marker);
        }
    }

    private static bool IsKnownToCrash(string testClass, string testMethod)
    {
        foreach (string line in KnownCrashers.Value)
        {
            string[] parts = line.Split('\t');
            if (parts.Length >= 2 && parts[^2] == testClass && parts[^1] == testMethod)
            {
                return true;
            }
        }

        return false;
    }

    private static string[] ReadKnownCrashers()
        => File.Exists(TestsKnownToCrashTestRunnerFilePath)
            ? File.ReadAllLines(TestsKnownToCrashTestRunnerFilePath)
            : [];
}
