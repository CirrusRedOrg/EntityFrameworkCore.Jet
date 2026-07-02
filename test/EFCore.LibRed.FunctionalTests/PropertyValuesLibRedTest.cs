// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests
{
    public class PropertyValuesLibRedTest(PropertyValuesLibRedTest.PropertyValuesLibRedFixture fixture)
        : PropertyValuesRelationalTestBase<PropertyValuesLibRedTest.PropertyValuesLibRedFixture>(fixture)
    {
        public class PropertyValuesLibRedFixture : PropertyValuesRelationalFixture
        {
            protected override ITestStoreFactory TestStoreFactory => LibRedTestStoreFactory.Instance;

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
