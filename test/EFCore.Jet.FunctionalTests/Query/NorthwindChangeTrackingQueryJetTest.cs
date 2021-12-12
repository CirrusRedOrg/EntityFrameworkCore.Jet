// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class NorthwindChangeTrackingQueryJetTest : NorthwindChangeTrackingQueryTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public NorthwindChangeTrackingQueryJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture)
            : base(fixture)
        {
        }

        protected override NorthwindContext CreateNoTrackingContext()
            => new NorthwindRelationalContext(
                new DbContextOptionsBuilder(Fixture.CreateOptions())
                    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
    }
}
