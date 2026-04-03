using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

public class JetXunitTestRunner(
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
    : XunitTestRunner(test,
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
    private const int ResourceExceededMaxRetries = 3;
    private const int ResourceExceededDelayMilliseconds = 5000;

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

                if (!MessageBus.QueueMessage(new TestSkipped(Test, SkipReason)))
                {
                    CancellationTokenSource.Cancel();
                }
            }
            else
            {
                var aggregator = new ExceptionAggregator(Aggregator);

                if (!aggregator.HasExceptions)
                {
                    // Retry loop for transient \"system resource exceeded\" errors.
                    for (int attempt = 1; attempt <= ResourceExceededMaxRetries; attempt++)
                    {
                        var attemptAggregator = new ExceptionAggregator(aggregator);
                        var tuple = await attemptAggregator.RunAsync(() => InvokeTestAsync(attemptAggregator));

                        var attemptException = attemptAggregator.ToException();
                        if (attemptException != null && ContainsSystemResourceExceeded(attemptException))
                        {
                            if (attempt < ResourceExceededMaxRetries)
                            {
                                // Pause then retry.
                                await Task.Delay(ResourceExceededDelayMilliseconds, CancellationTokenSource.Token);
                                continue;
                            }
                        }

                        // Either success, non-retryable failure, or final failed retry.
                        if (tuple != null)
                        {
                            runSummary.Time = tuple.Item1;
                            output = tuple.Item2;
                        }

                        // Merge attempt exceptions back into main aggregator.
                        aggregator.Add(attemptAggregator.ToException());

                        break;
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

            if (Aggregator.HasExceptions &&
                !MessageBus.QueueMessage(new TestCleanupFailure(Test, Aggregator.ToException())))
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

    private static bool ContainsSystemResourceExceeded(Exception exception)
    {
        const string marker = "system resource exceeded";
        var aggregate = exception as AggregateException ?? new AggregateException(exception);
        foreach (var inner in aggregate.Flatten().InnerExceptions.SelectMany(e => e.FlattenHierarchy()))
        {
            if (inner.Message.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
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
            if (innerException is InvalidOperationException or OleDbException or OdbcException or NotSupportedException)
            {
                var message = innerException.Message;

                if (message.ToLower().StartsWith("jet does not support "))
                {
                    var expectedUnsupportedTranslation = message.Contains("APPLY statements") ||
                                                         message.Contains("skipping rows") || message.Contains("sequences");

                    skip = expectedUnsupportedTranslation;
                    unexpectedUnsupportedTranslation = !expectedUnsupportedTranslation;
                }
                else if (message.StartsWith("Unsupported Jet expression"))
                {
                    skip = true;
                }
                else if (message.StartsWith("No value given for one or more required parameters."))
                {
                    skip = true;
                }
                else if (message.StartsWith("Syntax error in PARAMETER clause"))
                {
                    skip = true;
                }

                if (skip)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(message.ReplaceLineEndings(" "));
                    sb.AppendLine("-----");

                    File.AppendAllText("ExpectedUnsupportedTranslations.txt", sb.ToString());

                    break;
                }

                if (unexpectedUnsupportedTranslation)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(message.ReplaceLineEndings(" "));
                    sb.AppendLine("-----");

                    File.AppendAllText("UnexpectedUnsupportedTranslations.txt", sb.ToString());
                }
            }
        }

        return skip;
    }
}