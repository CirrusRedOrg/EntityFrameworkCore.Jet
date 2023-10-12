using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

public class JetXunitTestRunner : XunitTestRunner
{
    public JetXunitTestRunner(
        ITest test,
        IMessageBus messageBus,
        Type testClass,
        object[] constructorArguments,
        MethodInfo testMethod,
        object[] testMethodArguments,
        string skipReason,
        IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        : base(
            test,
            messageBus,
            testClass,
            constructorArguments,
            testMethod,
            testMethodArguments,
            skipReason,
            beforeAfterAttributes,
            aggregator,
            cancellationTokenSource)
    {
    }

    public new async Task<RunSummary> RunAsync()
    {
        var runSummary = new RunSummary { Total = 1 };
        var output = string.Empty;

        if (!MessageBus.QueueMessage(new TestStarting(Test)))
        {
            CancellationTokenSource.Cancel();
        }
        else
        {
            AfterTestStarting();

            if (!string.IsNullOrEmpty(SkipReason))
            {
                ++runSummary.Skipped;

                if (!MessageBus.QueueMessage(
                        new TestSkipped(Test, SkipReason)))
                {
                    CancellationTokenSource.Cancel();
                }
            }
            else
            {
                var aggregator = new ExceptionAggregator(Aggregator);
                if (!aggregator.HasExceptions)
                {
                    var tuple = await aggregator.RunAsync(() => InvokeTestAsync(aggregator));
                    if (tuple != null)
                    {
                        runSummary.Time = tuple.Item1;
                        output = tuple.Item2;
                    }
                }

                TestResultMessage testResultMessage;

                var exception = aggregator.ToException();
                if (exception == null)
                {
                    testResultMessage = new TestPassed(Test, runSummary.Time, output);
                }
                    
                #region Customized
                /// This is what we are after. Mark failed tests as 'Skipped', if their failure is expected.
                else if (SkipFailedTest(exception))
                {
                    testResultMessage = new TestSkipped(Test, exception.Message);
                    ++runSummary.Skipped;
                }
                #endregion Customized
                    
                else
                {
                    testResultMessage = new TestFailed(Test, runSummary.Time, output, exception);
                    ++runSummary.Failed;
                }

                if (!CancellationTokenSource.IsCancellationRequested &&
                    !MessageBus.QueueMessage(testResultMessage))
                {
                    CancellationTokenSource.Cancel();
                }
            }

            Aggregator.Clear();

            BeforeTestFinished();

            if (Aggregator.HasExceptions && !MessageBus.QueueMessage(
                    new TestCleanupFailure(Test, Aggregator.ToException())))
            {
                CancellationTokenSource.Cancel();
            }

            if (!MessageBus.QueueMessage(new TestFinished(Test, runSummary.Time, output)))
            {
                CancellationTokenSource.Cancel();
            }
        }

        return runSummary;
    }

    /// <summary>
    /// Mark failed tests as 'Skipped', it they failed because they use an expression, that is not supported by the underlying database
    /// server version.
    /// </summary>
    protected virtual bool SkipFailedTest(Exception exception)
    {
        var skip = true;
        var aggregateException = exception as AggregateException ??
                                 new AggregateException(exception);

        foreach (var innerException in aggregateException.InnerExceptions)
        {
            if (!skip ||
                innerException is not InvalidOperationException)
            {
                return false;
            }

            if (innerException.Message.StartsWith("The LINQ expression '") ||
                innerException.Message.Contains("' could not be translated."))
            {
                skip &= /*innerException.Message.Contains("OUTER APPLY") ||
                        innerException.Message.Contains("CROSS APPLY") ||*/
                        innerException.Message.Contains("RowNumberExpression");

                var message = innerException.Message;
                skip = skip && true;
            }
        }

        return skip;
    }
}