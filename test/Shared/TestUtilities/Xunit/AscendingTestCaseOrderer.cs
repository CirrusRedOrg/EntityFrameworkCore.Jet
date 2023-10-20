using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

public class AscendingTestCaseOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        var orderTestCases = testCases.OrderBy(c => c.TestMethod.TestClass.Class.Name, StringComparer.Ordinal)
            .ThenBy(c => c.DisplayName, StringComparer.Ordinal)
            .ToList();
        
        return orderTestCases;
    }
}