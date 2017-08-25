using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFramework.Jet.FunctionalTests
{
    public class OptimisticConcurrencyJetTest : OptimisticConcurrencyTestBase<JetTestStore, F1JetFixture>
    {
        public OptimisticConcurrencyJetTest(F1JetFixture fixture)
            : base(fixture)
        {
        }

        protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
            => facade.UseTransaction(transaction.GetDbTransaction());
    }
}
