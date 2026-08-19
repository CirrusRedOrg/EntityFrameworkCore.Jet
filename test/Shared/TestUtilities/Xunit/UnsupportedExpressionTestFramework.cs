using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Sdk;
using Xunit.v3;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

/// <summary>
/// The default xunit framework, with failures the provider has declared it cannot translate reported as
/// skipped rather than failed. See <see cref="UnsupportedExpressionSkipPolicy" /> for which those are.
/// </summary>
/// <remarks>
/// A test that asks for something the provider has no SQL for has not found a defect, so reporting it as a
/// failure buries the ones that have. It cannot be handled by a <see cref="BeforeAfterTestAttribute" />,
/// because that sees only that a test ran, never why it failed — the exception reaches us solely as a
/// reported result. So the one thing overridden here is the message sink, and everything else is xunit's
/// own behaviour untouched.
/// </remarks>
public class UnsupportedExpressionTestFramework : XunitTestFramework
{
    protected override ITestFrameworkExecutor CreateExecutor(Assembly assembly)
        => new SkipPolicyExecutor(base.CreateExecutor(assembly));

    private sealed class SkipPolicyExecutor(ITestFrameworkExecutor inner) : ITestFrameworkExecutor
    {
        public ValueTask RunTestCases(
            IReadOnlyCollection<ITestCase> testCases,
            IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions,
            CancellationToken? cancellationToken)
            => inner.RunTestCases(
                testCases, new SkipPolicyMessageSink(executionMessageSink), executionOptions, cancellationToken);
    }

    private sealed class SkipPolicyMessageSink(IMessageSink inner) : IMessageSink
    {
        public bool OnMessage(IMessageSinkMessage message)
        {
            if (message is ITestFailed failed
                && UnsupportedExpressionSkipPolicy.ShouldSkip(failed.ExceptionTypes, failed.Messages))
            {
                message = ToSkipped(failed);
            }

            return inner.OnMessage(message);
        }

        private static TestSkipped ToSkipped(ITestFailed failed)
            => new()
            {
                // The reason names the provider's own wording, so the skip says which construct was missing.
                Reason = failed.Messages.Length > 0 ? failed.Messages[0] : "Unsupported by the provider.",
                AssemblyUniqueID = failed.AssemblyUniqueID,
                TestCollectionUniqueID = failed.TestCollectionUniqueID,
                TestClassUniqueID = failed.TestClassUniqueID,
                TestMethodUniqueID = failed.TestMethodUniqueID,
                TestCaseUniqueID = failed.TestCaseUniqueID,
                TestUniqueID = failed.TestUniqueID,
                ExecutionTime = failed.ExecutionTime,
                Output = failed.Output,
                Warnings = failed.Warnings,
                FinishTime = failed.FinishTime
            };
    }
}
