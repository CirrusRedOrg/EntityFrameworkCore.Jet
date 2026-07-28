using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.v3;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

public class AscendingTestCollectionOrderer : ITestCollectionOrderer
{
    public IReadOnlyCollection<ITestCollection> OrderTestCollections(IReadOnlyCollection<ITestCollection> testCollections)
    {
        var orderTestCollections = testCollections.OrderBy(c => c.TestCollectionDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        
        return orderTestCollections;
    }
}