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
            //No spatial type support in Jet
            typeof(SpatialQueryRelationalTestBase<>),
            typeof(SpatialQueryTestBase<>),
            typeof(SpatialTestBase<>),
            //Only very limited support for primitive collections.
            //Can read/write the whole field at a time but any query that needs access to a specific element will fail.
            typeof(NonSharedPrimitiveCollectionsQueryTestBase),
            typeof(NonSharedPrimitiveCollectionsQueryRelationalTestBase),
            //No Json query support in Jet
            typeof(JsonQueryTestBase<>),
            typeof(JsonUpdateTestBase<>),
            //Too complex table structure for Jet/MS Access. Too many indexes on table.
            //Caused by having too many navs (foreign keys) on a single table.
            //Also having a primary key (and its related foreign keys) being over more than 14 fields.
            typeof(ComplexNavigationsSharedTypeQueryRelationalTestBase<>),
            typeof(ComplexNavigationsSharedTypeQueryTestBase<>),
            typeof(ComplexNavigationsCollectionsSharedTypeQueryRelationalTestBase<>),
            typeof(ComplexNavigationsCollectionsSharedTypeQueryTestBase<>),
            typeof(ComplexNavigationsCollectionsSplitSharedTypeQueryRelationalTestBase<>),
            typeof(UpdatesTestBase<>),
            typeof(UpdatesRelationalTestBase<>),
            //No user defined functions in MS Access/Jet
            typeof(UdfDbFunctionTestBase<>),
        };

        protected override Assembly TargetAssembly { get; } = typeof(JetComplianceTest).Assembly;

    }
}
