using System;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFramework.Jet.FunctionalTests
{
    public class PropertyValuesJetTest
        : PropertyValuesTestBase<JetTestStore, PropertyValuesJetTest.PropertyValuesJetFixture>
    {
        public PropertyValuesJetTest(PropertyValuesJetFixture fixture)
            : base(fixture)
        {
        }

        public class PropertyValuesJetFixture : PropertyValuesFixtureBase
        {
            private const string DatabaseName = "PropertyValues";

            private readonly IServiceProvider _serviceProvider;

            public PropertyValuesJetFixture()
            {
                _serviceProvider = new ServiceCollection()
                    .AddEntityFrameworkJet()
                    .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                    .BuildServiceProvider();
            }

            public override JetTestStore CreateTestStore()
            {
                return JetTestStore.GetOrCreateShared(DatabaseName, () =>
                {
                    var optionsBuilder = new DbContextOptionsBuilder()
                        .UseJet(JetTestStore.CreateConnectionString(DatabaseName))
                        .UseInternalServiceProvider(_serviceProvider);

                    using (var context = new AdvancedPatternsMasterContext(optionsBuilder.Options))
                    {
                        context.Database.EnsureClean();
                        Seed(context);
                    }
                });
            }

            public override DbContext CreateContext(JetTestStore testStore)
            {
                var optionsBuilder = new DbContextOptionsBuilder()
                    .UseJet(testStore.Connection)
                    .UseInternalServiceProvider(_serviceProvider);

                var context = new AdvancedPatternsMasterContext(optionsBuilder.Options);
                context.Database.UseTransaction(testStore.Transaction);

                return context;
            }
        }
    }
}
