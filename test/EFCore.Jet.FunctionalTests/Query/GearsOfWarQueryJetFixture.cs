// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.GearsOfWarModel;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class GearsOfWarQueryJetFixture : GearsOfWarQueryRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);

            modelBuilder.Entity<City>().Property(g => g.Location).HasColumnType("varchar(100)");

            // No support yet for DateOnly/TimeOnly (#24507)
            modelBuilder.Entity<Mission>(
                b =>
                {
                    b.Ignore(m => m.Date);
                    b.Ignore(m => m.Time);
                });
        }
    }
}
