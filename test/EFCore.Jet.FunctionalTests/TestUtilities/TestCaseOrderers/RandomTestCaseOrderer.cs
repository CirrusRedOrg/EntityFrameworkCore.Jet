using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.TestCaseOrderers
{
    public class RandomTestCaseOrderer : ITestCaseOrderer
    {
        public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
            where TTestCase : ITestCase
        {
            var random = new Random();
            var orderedTestCases = testCases.OrderBy(c => random.NextDouble()).ToList();

            var builder = new StringBuilder()
                .AppendLine("Test Case Order:")
                .AppendLine(string.Join(Environment.NewLine, orderedTestCases.Select(c => c.TestMethod.Method.Name)));
            
            Debug.WriteLine(builder);
            Console.WriteLine(builder);
            
            return orderedTestCases;
        }
    }
}