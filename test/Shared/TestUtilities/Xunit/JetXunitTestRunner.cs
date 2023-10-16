using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
                /// This is what we are after. Mark failed tests as 'Skipped', if the failure is expected.
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
    /// Mark failed tests as 'Skipped', if they failed because they use an expression, that we explicitly marked as
    /// supported by Jet.
    /// </summary>
    protected virtual bool SkipFailedTest(Exception exception)
    {
        var skip = false;
        var unexpectedUnsupportedTranslation = false;
        
        var aggregateException = exception as AggregateException ??
                                 new AggregateException(exception);

        foreach (var innerException in aggregateException.Flatten().InnerExceptions.SelectMany(e => e.FlattenHierarchy()))
        {
            if (innerException is InvalidOperationException)
            {
                var message = innerException.Message;
                
                if (message.StartsWith("Jet does not support "))
                {
                    var expectedUnsupportedTranslation = message.Contains("APPLY statements") ||
                                                         message.Contains("skipping rows");

                    skip = expectedUnsupportedTranslation;
                    unexpectedUnsupportedTranslation = !expectedUnsupportedTranslation;
                }
                else if (message.StartsWith("The LINQ expression '") &&
                         message.Contains("' could not be translated."))
                {
                    var expectedUnsupportedTranslation = message.Contains("RowNumberExpression");

                    skip = expectedUnsupportedTranslation;
                    unexpectedUnsupportedTranslation = !expectedUnsupportedTranslation;
                }

                if (skip)
                {
                    // var sb = new StringBuilder();
                    // sb.AppendLine(message.ReplaceLineEndings(" "));
                    // sb.AppendLine("-----");
                    //
                    // File.AppendAllText("ExpectedUnsupportedTranslations.txt", sb.ToString());

                    break;
                }

                if (unexpectedUnsupportedTranslation)
                {
                    // var sb = new StringBuilder();
                    // sb.AppendLine(message.ReplaceLineEndings(" "));
                    // sb.AppendLine("-----");
                    //
                    // File.AppendAllText("UnsupportedTranslations.txt", sb.ToString());
                }
            }
        }

        return skip;
    }
}