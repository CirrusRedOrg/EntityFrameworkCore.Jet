using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

public class JetXunitTestCaseRunner : XunitTestCaseRunner
{
    public const string TestRunnerCrashCacheDirectory = "TestRunnerCrashCache";
    public const string AutoSkipPrefix = "[AutoSkip]";
    public const string AutoSkipTestRunnerCrashingTestsEnvironmentVariableName = "EFCoreJet_AutoSkipTestRunnerCrashingTests";

    public virtual bool EnableAutoSkipTestsKnownToCrashTestRunner
        => (Environment.GetEnvironmentVariable(AutoSkipTestRunnerCrashingTestsEnvironmentVariableName)?.ToLowerInvariant() ?? "true") != "false";

    public JetXunitTestCaseRunner(IXunitTestCase testCase,
        string displayName,
        string skipReason,
        object[] constructorArguments,
        object[] testMethodArguments,
        IMessageBus messageBus,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        : base(
            testCase,
            displayName,
            skipReason,
            constructorArguments,
            testMethodArguments,
            messageBus,
            aggregator,
            cancellationTokenSource)
    {
    }

    protected override XunitTestRunner CreateTestRunner(ITest test,
        IMessageBus messageBus,
        Type testClass,
        object[] constructorArguments,
        MethodInfo testMethod,
        object[] testMethodArguments,
        string skipReason,
        IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        => new JetXunitTestRunner(
            test,
            messageBus,
            testClass,
            constructorArguments,
            testMethod,
            testMethodArguments,
            skipReason,
            beforeAfterAttributes,
            new ExceptionAggregator(aggregator),
            cancellationTokenSource);

    /// <remarks>
    /// `TestRunner&lt;TTestCase&gt;.RunAsync()` is not virtual, so we need to override this method here to call our own
    /// `JetXunitTestRunner.RunAsync()` implementation.
    /// </remarks>>
    protected override async Task<RunSummary> RunTestAsync()
    {
        if (EnableAutoSkipTestsKnownToCrashTestRunner)
        {
            AutoSkipTestsKnownToCrashTestRunner();
        }

        return await RunWithCrashDetection(
            () => ((JetXunitTestRunner)CreateTestRunner(
                    CreateTest(TestCase, DisplayName),
                    MessageBus,
                    TestClass,
                    ConstructorArguments,
                    TestMethod,
                    TestMethodArguments,
                    SkipReason,
                    BeforeAfterAttributes,
                    Aggregator,
                    CancellationTokenSource))
                .RunAsync());
    }

    protected virtual async Task<RunSummary> RunWithCrashDetection(Func<Task<RunSummary>> func)
    {
        Directory.CreateDirectory(TestRunnerCrashCacheDirectory);

        var filePath = Path.Combine(TestRunnerCrashCacheDirectory, $"{DateTime.UtcNow:yyyyMMdd'_'HHmmss.fffffff}_{(Environment.Is64BitProcess ? "x64" : "x86")}_{Guid.NewGuid()}.txt");
        var contents = $"{TestCase.TestMethod.TestClass.Class.Name}\t{TestCase.TestMethod.Method.Name}";
        await File.WriteAllTextAsync(filePath, contents);

        var result = await func();

        File.Delete(filePath);

        return result;
    }

    protected virtual void AutoSkipTestsKnownToCrashTestRunner()
    {
        if (IsTestKnownToCrashTestRunner(TestCase))
        {
            SkipReason = $"{AutoSkipPrefix} {TestRunnerCrashAttribute.DefaultSkipReason}";
        }
    }

    protected virtual bool IsTestKnownToCrashTestRunner(ITestCase testCase)
    {
        if (File.Exists(JetXunitTestFramework.TestsKnownToCrashTestRunnerFilePath))
        {
            foreach (var line in File.ReadLines(JetXunitTestFramework.TestsKnownToCrashTestRunnerFilePath))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2)
                {
                    var testClass = parts[^2];
                    var testMethod = parts[^1];

                    if (testClass == testCase.TestMethod.TestClass.Class.Name &&
                        testMethod == testCase.TestMethod.Method.Name)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}