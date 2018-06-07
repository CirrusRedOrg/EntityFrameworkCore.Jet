using System;
using EntityFramework.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFramework.Jet.FunctionalTests
{
    public class PropertyValuesJetTest
        : PropertyValuesTestBase<PropertyValuesJetTest.PropertyValuesJetFixture>
    {
        public PropertyValuesJetTest(PropertyValuesJetFixture fixture)
            : base(fixture)
        {
        }

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
