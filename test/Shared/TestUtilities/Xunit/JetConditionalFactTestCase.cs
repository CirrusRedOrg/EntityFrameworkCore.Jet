﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.TestUtilities.Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

/// <remarks>
/// We cannot inherit from ConditionalFactTestCase, because it's sealed.
/// </remarks>
public sealed class JetConditionalFactTestCase : XunitTestCase
{
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public JetConditionalFactTestCase()
    {
    }

    public JetConditionalFactTestCase(
        IMessageSink diagnosticMessageSink,
        TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions,
        ITestMethod testMethod,
        object[]? testMethodArguments = null)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
    {
    }

    public override async Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink,
        IMessageBus messageBus,
        object[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        => await XunitTestCaseExtensions.TrySkipAsync(this, messageBus)
            ? new RunSummary { Total = 1, Skipped = 1 }
            : await new JetXunitTestCaseRunner(
                this,
                DisplayName,
                SkipReason,
                constructorArguments,
                TestMethodArguments,
                messageBus,
                aggregator,
                cancellationTokenSource).RunAsync();
}