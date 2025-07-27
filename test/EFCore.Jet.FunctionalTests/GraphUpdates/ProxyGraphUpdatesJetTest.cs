// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public abstract class ProxyGraphUpdatesJetTest
    {
        public abstract class ProxyGraphUpdatesJetTestBase<TFixture>(TFixture fixture)
            : ProxyGraphUpdatesTestBase<TFixture>(fixture)
            where TFixture : ProxyGraphUpdatesJetTestBase<TFixture>.ProxyGraphUpdatesJetFixtureBase, new()
        {
            protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
                => facade.UseTransaction(transaction.GetDbTransaction());

            public abstract class ProxyGraphUpdatesJetFixtureBase : ProxyGraphUpdatesFixtureBase
            {
                public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;
                protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

                protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
                {
                    base.OnModelCreating(modelBuilder, context);
                    modelBuilder.Entity<SharedFkRoot>().Property(x => x.Id).HasColumnType("int");
                    modelBuilder.Entity<SharedFkDependant>().Property(x => x.Id).HasColumnType("int");
                    modelBuilder.Entity<SharedFkParent>().Property(x => x.Id).HasColumnType("int");
                }

                protected override async Task SeedAsync(DbContext context)
                {
                    //context.Database.ExecuteSql($"ALTER TABLE `SharedFkDependant` DROP CONSTRAINT `AK_SharedFkDependant_RootId_Id`");
                    await base.SeedAsync(context);

                    await context.Database.ExecuteSqlAsync($"ALTER TABLE `OptionalComposite2` DROP CONSTRAINT `FK_OptionalComposite2_OptionalAk1_ParentId_ParentAlternateId`");
                    await context.Database.ExecuteSqlAsync($"ALTER TABLE `OptionalOverlapping2` DROP CONSTRAINT `FK_OptionalOverlapping2_RequiredComposite1_ParentId_ParentAlter~`");
                    await context.Database.ExecuteSqlAsync($"ALTER TABLE `OptionalSingleComposite2` DROP CONSTRAINT `FK_OptionalSingleComposite2_OptionalSingleAk1_BackId_ParentAlte~`");
                    await context.Database.ExecuteSqlAsync($"ALTER TABLE `RequiredComposite2` DROP CONSTRAINT `FK_RequiredComposite2_RequiredAk1_ParentId_ParentAlternateId`");
                    await context.Database.ExecuteSqlAsync($"ALTER TABLE `SharedFkParent` DROP CONSTRAINT `FK_SharedFkParent_SharedFkDependant_RootId_DependantId`");
                }
            }
        }

        public class LazyLoading(LazyLoading.ProxyGraphUpdatesWithLazyLoadingJetFixture fixture)
            : ProxyGraphUpdatesJetTestBase<LazyLoading.ProxyGraphUpdatesWithLazyLoadingJetFixture>(fixture)
        {
            protected override bool DoesLazyLoading => true;
            protected override bool DoesChangeTracking => false;

            public class ProxyGraphUpdatesWithLazyLoadingJetFixture : ProxyGraphUpdatesJetFixtureBase
            {
                protected override string StoreName { get; } = "ProxyGraphLazyLoadingUpdatesTest";

                public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
                    => base.AddOptions(builder.UseLazyLoadingProxies());

                protected override IServiceCollection AddServices(IServiceCollection serviceCollection)
                    => base.AddServices(serviceCollection.AddEntityFrameworkProxies());

                protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
                {
                    modelBuilder.UseJetIdentityColumns();

                    base.OnModelCreating(modelBuilder, context);

                    modelBuilder.Entity<SharedFkRoot>().Property(x => x.Id).HasColumnType("int");
                    modelBuilder.Entity<SharedFkDependant>().Property(x => x.Id).HasColumnType("int");
                    modelBuilder.Entity<SharedFkParent>().Property(x => x.Id).HasColumnType("int");
                }
            }
        }

        public class ChangeTracking(ChangeTracking.ProxyGraphUpdatesWithChangeTrackingJetFixture fixture)
            : ProxyGraphUpdatesJetTestBase<ChangeTracking.ProxyGraphUpdatesWithChangeTrackingJetFixture>(fixture)
        {
            protected override bool DoesLazyLoading => false;
            protected override bool DoesChangeTracking => true;

            public class ProxyGraphUpdatesWithChangeTrackingJetFixture : ProxyGraphUpdatesJetFixtureBase
            {
                protected override string StoreName { get; } = "ProxyGraphChangeTrackingUpdatesTest";

                public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
                    => base.AddOptions(builder.UseChangeTrackingProxies());

                protected override IServiceCollection AddServices(IServiceCollection serviceCollection)
                    => base.AddServices(serviceCollection.AddEntityFrameworkProxies());

                protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
                {
                    modelBuilder.UseJetIdentityColumns();

                    base.OnModelCreating(modelBuilder, context);

                    modelBuilder.Entity<SharedFkRoot>().Property(x => x.Id).HasColumnType("int");
                    modelBuilder.Entity<SharedFkDependant>().Property(x => x.Id).HasColumnType("int");
                    modelBuilder.Entity<SharedFkParent>().Property(x => x.Id).HasColumnType("int");
                }
            }
        }

        public class ChangeTrackingAndLazyLoading(
            ChangeTrackingAndLazyLoading.ProxyGraphUpdatesWithChangeTrackingAndLazyLoadingJetFixture fixture)
            : ProxyGraphUpdatesJetTestBase<
                ChangeTrackingAndLazyLoading.ProxyGraphUpdatesWithChangeTrackingAndLazyLoadingJetFixture>(fixture)
        {
            protected override bool DoesLazyLoading => true;
            protected override bool DoesChangeTracking => true;

            public class ProxyGraphUpdatesWithChangeTrackingAndLazyLoadingJetFixture : ProxyGraphUpdatesJetFixtureBase
            {
                protected override string StoreName { get; } = "ProxyGraphChangeTrackingAndLazyLoadingUpdatesTest";

                public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
                    => base.AddOptions(builder.UseLazyLoadingProxies().UseChangeTrackingProxies());

                protected override IServiceCollection AddServices(IServiceCollection serviceCollection)
                    => base.AddServices(serviceCollection.AddEntityFrameworkProxies());

                protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
                {
                    modelBuilder.UseJetIdentityColumns();

                    base.OnModelCreating(modelBuilder, context);

                    modelBuilder.Entity<SharedFkRoot>().Property(x => x.Id).HasColumnType("int");
                    modelBuilder.Entity<SharedFkDependant>().Property(x => x.Id).HasColumnType("int");
                    modelBuilder.Entity<SharedFkParent>().Property(x => x.Id).HasColumnType("int");
                }
            }
        }
    }
}
