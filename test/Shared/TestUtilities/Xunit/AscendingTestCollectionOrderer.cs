using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

public class AscendingTestCollectionOrderer : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
    {
        var orderTestCollections = testCollections.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        
        return orderTestCollections;
    }
}