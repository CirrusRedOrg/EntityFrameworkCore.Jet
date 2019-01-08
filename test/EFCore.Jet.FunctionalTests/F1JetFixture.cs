using System;
using EntityFramework.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Metadata.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestModels.ConcurrencyModel;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFramework.Jet.FunctionalTests
{
    public class F1JetFixture : F1RelationalFixture
    {

        protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
        public override ModelBuilder CreateModelBuilder()
        {
            return new ModelBuilder(JetConventionSetBuilder.Build());
        }

        protected override void BuildModelExternal(ModelBuilder modelBuilder)
        {
            base.BuildModelExternal(modelBuilder);

            modelBuilder.Entity<Chassis>().Property<byte[]>("Version").IsRowVersion();
            modelBuilder.Entity<Driver>().Property<byte[]>("Version").IsRowVersion();

            modelBuilder.Entity<Team>().Property<byte[]>("Version")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            modelBuilder.Entity<TitleSponsor>()
                .OwnsOne(s => s.Details)
                .Property(d => d.Space).HasColumnType("decimal(18,2)");
        }
    }
}