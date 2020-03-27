// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.TestModels.ConcurrencyModel;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class F1JetFixture : F1RelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

        public override ModelBuilder CreateModelBuilder()
            => new ModelBuilder(JetConventionSetBuilder.Build());

        protected override void BuildModelExternal(ModelBuilder modelBuilder)
        {
            base.BuildModelExternal(modelBuilder);

            modelBuilder.Entity<Chassis>().Property<byte[]>("Version").IsRowVersion();
            modelBuilder.Entity<Driver>().Property<byte[]>("Version").IsRowVersion();

            modelBuilder.Entity<Team>().Property<byte[]>("Version")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            modelBuilder.Entity<Sponsor>(
                eb =>
                {
                    eb.Property<byte[]>("Version").IsRowVersion().HasColumnName("Version");
                    eb.Property<int?>(Sponsor.ClientTokenPropertyName).HasColumnName(Sponsor.ClientTokenPropertyName);
                });
            modelBuilder.Entity<TitleSponsor>()
                .OwnsOne(
                    s => s.Details, eb =>
                    {
                        eb.Property(d => d.Space).HasColumnType("decimal(18,2)");
                        eb.Property<byte[]>("Version").IsRowVersion().HasColumnName("Version");
                        eb.Property<int?>(Sponsor.ClientTokenPropertyName).IsConcurrencyToken()
                            .HasColumnName(Sponsor.ClientTokenPropertyName);
                    });
        }
    }
}
