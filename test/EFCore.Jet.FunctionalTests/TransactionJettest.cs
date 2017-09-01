using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class TransactionJetTest : TransactionTestBase<JetTestStore, TransactionJetFixture>
    {
        public TransactionJetTest(TransactionJetFixture fixture)
            : base(fixture)
        {
        }

        protected override bool SnapshotSupported => false;


        [Theory(Skip = "Unsupported by JET")]
        public override Task Can_use_open_connection_with_started_transaction(bool a) { return Task.CompletedTask; }
        [Theory(Skip = "Unsupported by JET")]
        public override Task QueryAsync_uses_explicit_transaction(bool a) { return Task.CompletedTask; }
        [Theory(Skip = "Unsupported by JET")]
        public override void Query_uses_explicit_transaction(bool a) { }

        [Theory(Skip = "Unsupported by JET")]
        public override void UseTransaction_throws_if_another_transaction_started(bool a) { }
        [Theory(Skip = "Unsupported by JET")]
        public override void UseTransaction_throws_if_mismatched_connection(bool a) { }
        [Theory(Skip = "Unsupported by JET")]
        public override void UseTransaction_will_not_dispose_external_transaction(bool a) { }


    }
}
