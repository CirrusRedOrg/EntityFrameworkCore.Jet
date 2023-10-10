// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class JetComplianceTest : RelationalComplianceTestBase
    {

        protected override ICollection<Type> IgnoredTestBases { get; } = new HashSet<Type>
        {
            typeof(SpatialQueryRelationalTestBase<>),
            typeof(Microsoft.EntityFrameworkCore.Query.SpatialQueryTestBase<>),
            typeof(Microsoft.EntityFrameworkCore.SpatialTestBase<>),
            typeof(PrimitiveCollectionsQueryTestBase<>),
            typeof(NonSharedPrimitiveCollectionsQueryTestBase),
            typeof(NonSharedPrimitiveCollectionsQueryRelationalTestBase),
            //Gears of War is our own customized version so we can ignore the Microsoft TestBase classes
            typeof(Microsoft.EntityFrameworkCore.Query.GearsOfWarQueryTestBase<>),
            typeof(Microsoft.EntityFrameworkCore.Query.TPTGearsOfWarQueryRelationalTestBase<>),
            typeof(Microsoft.EntityFrameworkCore.Query.GearsOfWarQueryRelationalTestBase<>),
            typeof(Microsoft.EntityFrameworkCore.Query.TPCGearsOfWarQueryRelationalTestBase<>),
            typeof(Microsoft.EntityFrameworkCore.Query.GearsOfWarFromSqlQueryTestBase<>)
        };

        protected override Assembly TargetAssembly { get; } = typeof(JetComplianceTest).Assembly;

    }
}
