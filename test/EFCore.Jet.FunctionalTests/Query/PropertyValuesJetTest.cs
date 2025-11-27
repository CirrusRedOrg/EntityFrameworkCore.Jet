using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore;

#nullable disable

public class PropertyValuesJetTest(PropertyValuesJetTest.PropertyValuesJetFixture fixture)
    : PropertyValuesRelationalTestBase<PropertyValuesJetTest.PropertyValuesJetFixture>(fixture)
{
    public class PropertyValuesJetFixture : PropertyValuesRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory
            => JetTestStoreFactory.Instance;

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
