// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class PropertyValuesJetTest(PropertyValuesJetTest.PropertyValuesJetFixture fixture)
        : PropertyValuesTestBase<PropertyValuesJetTest.PropertyValuesJetFixture>(fixture)
    {
        public class PropertyValuesJetFixture : PropertyValuesFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

            protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
            {
                base.OnModelCreating(modelBuilder, context);

                modelBuilder.Entity<Building>()
                    .Property(b => b.Value).HasColumnType("decimal(18,2)");

                modelBuilder.Entity<CurrentEmployee>()
                    .Property(ce => ce.LeaveBalance).HasColumnType("decimal(18,2)");
            }
        }
    }
}
