// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public abstract class ProxyGraphUpdatesJetTest
    {
        public abstract class ProxyGraphUpdatesJetTestBase<TFixture> : ProxyGraphUpdatesTestBase<TFixture>
            where TFixture : ProxyGraphUpdatesJetTestBase<TFixture>.ProxyGraphUpdatesJetFixtureBase, new()
        {
            protected ProxyGraphUpdatesJetTestBase(TFixture fixture)
                : base(fixture)
            {
            }

            protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
                => facade.UseTransaction(transaction.GetDbTransaction());

            public abstract class ProxyGraphUpdatesJetFixtureBase : ProxyGraphUpdatesFixtureBase
            {
                public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;
                protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
            }
        }

        public class LazyLoading : ProxyGraphUpdatesJetTestBase<LazyLoading.ProxyGraphUpdatesWithLazyLoadingJetFixture>
        {
            public LazyLoading(ProxyGraphUpdatesWithLazyLoadingJetFixture fixture)
                : base(fixture)
            {
            }

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
                    modelBuilder.UseIdentityColumns();

                    base.OnModelCreating(modelBuilder, context);
                }
            }
        }

        public class ChangeTracking : ProxyGraphUpdatesJetTestBase<ChangeTracking.ProxyGraphUpdatesWithChangeTrackingJetFixture>
        {
            public ChangeTracking(ProxyGraphUpdatesWithChangeTrackingJetFixture fixture)
                : base(fixture)
            {
            }

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
                    modelBuilder.UseIdentityColumns();

                    base.OnModelCreating(modelBuilder, context);
                }
            }
        }

        public class ChangeTrackingAndLazyLoading : ProxyGraphUpdatesJetTestBase<ChangeTrackingAndLazyLoading.ProxyGraphUpdatesWithChangeTrackingAndLazyLoadingJetFixture>
        {
            public ChangeTrackingAndLazyLoading(ProxyGraphUpdatesWithChangeTrackingAndLazyLoadingJetFixture fixture)
                : base(fixture)
            {
            }

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
                    modelBuilder.UseIdentityColumns();

                    base.OnModelCreating(modelBuilder, context);
                }
            }
        }
    }
}
