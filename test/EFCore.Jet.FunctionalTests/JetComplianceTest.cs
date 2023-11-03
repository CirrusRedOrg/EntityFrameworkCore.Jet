using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Update;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class JetComplianceTest : RelationalComplianceTestBase
    {

        protected override ICollection<Type> IgnoredTestBases { get; } = new HashSet<Type>
        {
            typeof(SpatialQueryRelationalTestBase<>),
            typeof(SpatialQueryTestBase<>),
            typeof(SpatialTestBase<>),
            typeof(NonSharedPrimitiveCollectionsQueryTestBase),
            typeof(NonSharedPrimitiveCollectionsQueryRelationalTestBase),
            typeof(JsonQueryTestBase<>),
            typeof(JsonQueryAdHocTestBase),
            typeof(JsonUpdateTestBase<>)
        };

        protected override Assembly TargetAssembly { get; } = typeof(JetComplianceTest).Assembly;

    }
}
