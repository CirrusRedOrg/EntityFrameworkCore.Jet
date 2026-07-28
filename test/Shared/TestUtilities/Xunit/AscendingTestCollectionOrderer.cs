using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

public class AscendingTestCollectionOrderer : ITestCollectionOrderer
{
    // Explicit implementation: the interface's own generic constraint is inherited rather than restated,
    // which is what CS0425 was complaining about — it has to match exactly and is not visible from here.
    IReadOnlyCollection<TTestCollection> ITestCollectionOrderer.OrderTestCollections<TTestCollection>(
        IReadOnlyCollection<TTestCollection> testCollections)
    {
        var orderTestCollections = testCollections.OrderBy(c => (c as ITestCollectionMetadata)?.TestCollectionDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        
        return orderTestCollections;
    }
}