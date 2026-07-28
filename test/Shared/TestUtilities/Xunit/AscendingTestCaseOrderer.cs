using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Sdk;
using Xunit.v3;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

public class AscendingTestCaseOrderer : ITestCaseOrderer
{
    public IReadOnlyCollection<TTestCase> OrderTestCases<TTestCase>(IReadOnlyCollection<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        var orderTestCases = testCases.OrderBy(c => c.TestClassName, StringComparer.Ordinal)
            .ThenBy(c => c.TestCaseDisplayName, StringComparer.Ordinal)
            .ToList();
        
        return orderTestCases;
    }
}