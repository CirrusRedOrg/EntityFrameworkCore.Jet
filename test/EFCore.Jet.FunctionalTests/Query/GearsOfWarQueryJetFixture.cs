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
        }

        protected override void Seed(GearsOfWarContext context)
        {
            // Drop constraint to workaround Jet limitation regarding compound foreign keys and NULL.
            context.Database.ExecuteSql($"ALTER TABLE `Gears` DROP CONSTRAINT `FK_Gears_Gears_LeaderNickname_LeaderSquadId`");

            base.Seed(context);
        }
    }
}
