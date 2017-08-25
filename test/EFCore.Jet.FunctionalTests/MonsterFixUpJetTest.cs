using System;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.TestModels;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFramework.Jet.FunctionalTests
{
    public class MonsterFixupJetTest : MonsterFixupTestBase
    {
        protected override IServiceProvider CreateServiceProvider(bool throwingStateManager = false)
        {
            var serviceCollection = new ServiceCollection()
                .AddEntityFrameworkJet();

            if (throwingStateManager)
            {
                serviceCollection.AddScoped<IStateManager, ThrowingMonsterStateManager>();
            }

            return serviceCollection.BuildServiceProvider();
        }

        protected override DbContextOptions CreateOptions(string databaseName)
        {
            var optionsBuilder = new DbContextOptionsBuilder();
            optionsBuilder
                .UseJet(ConnectionStringBuilderHelper.GetJetConnectionString(databaseName));

            return optionsBuilder.Options;
        }

        private JetTestStore _testStore;

        protected override void CreateAndSeedDatabase(string databaseName, Func<MonsterContext> createContext, Action<MonsterContext> seed)
        {
            _testStore = JetTestStore.GetOrCreateShared(databaseName, () =>
            {
                using (var context = createContext())
                {
                    context.Database.EnsureCreated();
                    seed(context);
                }
            });
        }

        public virtual void Dispose() => _testStore?.Dispose();

        protected override void OnModelCreating<TMessage, TProductPhoto, TProductReview>(ModelBuilder builder)
        {
            base.OnModelCreating<TMessage, TProductPhoto, TProductReview>(builder);

            builder.Entity<TMessage>().HasKey(e => e.MessageId);
            builder.Entity<TProductPhoto>().HasKey(e => e.PhotoId);
            builder.Entity<TProductReview>().HasKey(e => e.ReviewId);
        }
    }
}
