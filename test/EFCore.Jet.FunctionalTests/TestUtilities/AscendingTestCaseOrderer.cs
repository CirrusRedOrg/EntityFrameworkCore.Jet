using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public class AscendingTestCaseOrderer : ITestCaseOrderer
    {
        public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
            where TTestCase : ITestCase
            => testCases.OrderBy(c => c.DisplayName);
    }
}