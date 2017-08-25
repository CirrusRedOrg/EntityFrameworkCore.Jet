using Microsoft.EntityFrameworkCore;

namespace EntityFramework.Jet.FunctionalTests
{
    public class TransactionJetTest : TransactionTestBase<JetTestStore, TransactionJetFixture>
    {
        public TransactionJetTest(TransactionJetFixture fixture)
            : base(fixture)
        {
        }

        protected override bool SnapshotSupported => false;
    }
}
