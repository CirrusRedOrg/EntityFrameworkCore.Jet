using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Xunit.Sdk;
using Xunit.v3;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

public class RandomTestCaseOrderer : ITestCaseOrderer
{
    public IReadOnlyCollection<TTestCase> OrderTestCases<TTestCase>(IReadOnlyCollection<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        var random = new Random();
        var orderedTestCases = testCases.OrderBy(c => random.NextDouble()).ToList();

        var builder = new StringBuilder()
            .AppendLine("Test Case Order:")
            .AppendLine(string.Join(Environment.NewLine, orderedTestCases.Select(c => c.TestMethodName)));
            
        Debug.WriteLine(builder);
        Console.WriteLine(builder);
            
        return orderedTestCases;
    }
}