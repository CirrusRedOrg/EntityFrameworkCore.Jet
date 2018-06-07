using System;
using EntityFramework.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFramework.Jet.FunctionalTests
{
    public class NorthwindQueryWithForcedClientEvalJetFixture<TModelCustomizer> : NorthwindQueryRelationalFixture<TModelCustomizer>, IDisposable
        where TModelCustomizer : IModelCustomizer, new()
    {
        private readonly DbContextOptions _options;

        private readonly JetTestStore _testStore = JetTestStore.GetNorthwindStore();

        protected virtual DbContextOptionsBuilder ConfigureOptions(DbContextOptionsBuilder dbContextOptionsBuilder)
            => dbContextOptionsBuilder;

        protected virtual void ConfigureOptions(JetDbContextOptionsBuilder sqlCeDbContextOptionsBuilder)
        {
        }

        protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
    }
}
