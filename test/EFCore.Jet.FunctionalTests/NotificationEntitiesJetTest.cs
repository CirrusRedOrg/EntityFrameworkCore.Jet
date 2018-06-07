using EntityFramework.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFramework.Jet.FunctionalTests
{
    public class NotificationEntitiesJetTest
        : NotificationEntitiesTestBase<NotificationEntitiesJetTest.NotificationEntitiesJetFixture>
    {
        public NotificationEntitiesJetTest(NotificationEntitiesJetFixture fixture)
            : base(fixture)
        {
        }

        public class NotificationEntitiesJetFixture : NotificationEntitiesFixtureBase
        {
            private const string DatabaseName = "NotificationEntities";
            private readonly DbContextOptions _options;

            public NotificationEntitiesJetFixture()
            {
                _options = new DbContextOptionsBuilder()
                    .UseJet(JetTestStore.CreateConnectionString(DatabaseName), b => b.ApplyConfiguration())
                    .UseInternalServiceProvider(new ServiceCollection()
                        .AddEntityFrameworkJet()
                        .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                        .BuildServiceProvider())
                    .Options;
            }

            public override DbContext CreateContext()
                => new DbContext(_options);

            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

        }
    }
}
