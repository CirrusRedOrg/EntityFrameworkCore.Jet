using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore.Internal;
using Xunit.Sdk;
using Xunit.v3;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

public class ExceptionTestCaseOrderer : ITestCaseOrderer
{
    private readonly string[] _testCaseOrder =
    [
        "Select_GetValueOrDefault_on_DateTime",
        "Select_byte_constant",
        "Select_DTO_with_member_init_distinct_in_subquery_translated_to_server",
        "Select_Except_reference_projection",
        "Select_Union",
        "Select_DTO_with_member_init_distinct_in_subquery_translated_to_server_2",
        "Select_bool_closure"
    ];
        
    public IReadOnlyCollection<TTestCase> OrderTestCases<TTestCase>(IReadOnlyCollection<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        var orderedTestCases = testCases.OrderBy(c => Array.IndexOf(_testCaseOrder, c.TestMethodName)).ToList();

        var builder = new StringBuilder()
            .AppendLine("Test Case Order:")
            .AppendLine(string.Join(Environment.NewLine, orderedTestCases.Select(c => c.TestMethodName)));
            
        Debug.WriteLine(builder);
        Console.WriteLine(builder);
            
        return orderedTestCases;
    }
}