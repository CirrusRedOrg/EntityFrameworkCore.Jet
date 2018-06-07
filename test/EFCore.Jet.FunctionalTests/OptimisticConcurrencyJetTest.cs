using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class OptimisticConcurrencyJetTest : OptimisticConcurrencyTestBase<F1JetFixture>
    {
        public OptimisticConcurrencyJetTest(F1JetFixture fixture)
            : base(fixture)
        {
        }

        protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
            => facade.UseTransaction(transaction.GetDbTransaction());


        [Fact(Skip = "Unsupported by JET: rowversion unsupported")]
        public override Task Change_in_independent_association_after_change_in_different_concurrency_token_results_in_independent_association_exception() { return Task.CompletedTask; }
        [Fact(Skip = "Unsupported by JET: rowversion unsupported")]
        public override Task Change_in_independent_association_results_in_independent_association_exception() { return Task.CompletedTask; }
        [Fact(Skip = "Unsupported by JET: rowversion unsupported")]
        public override Task Simple_concurrency_exception_can_be_resolved_with_client_values() { return Task.CompletedTask; }
        [Fact(Skip = "Unsupported by JET: rowversion unsupported")]
        public override Task Simple_concurrency_exception_can_be_resolved_with_new_values() { return Task.CompletedTask; }
        [Fact(Skip = "Unsupported by JET: rowversion unsupported")]
        public override Task Simple_concurrency_exception_can_be_resolved_with_store_values() { return Task.CompletedTask; }
        [Fact(Skip = "Unsupported by JET: rowversion unsupported")]
        public override Task Simple_concurrency_exception_can_be_resolved_with_store_values_using_Reload() { return Task.CompletedTask; }
        [Fact(Skip = "Unsupported by JET: rowversion unsupported")]
        public override Task Simple_concurrency_exception_can_be_resolved_with_store_values_using_equivalent_of_accept_changes() { return Task.CompletedTask; }
        [Fact(Skip = "Unsupported by JET: rowversion unsupported")]
        public override Task Two_concurrency_issues_in_one_to_many_related_entities_can_be_handled_by_dealing_with_dependent_first() { return Task.CompletedTask; }
        [Fact(Skip = "Unsupported by JET: rowversion unsupported")]
        public override Task Two_concurrency_issues_in_one_to_one_related_entities_can_be_handled_by_dealing_with_dependent_first() { return Task.CompletedTask; }
        [Fact(Skip = "Unsupported by JET: rowversion unsupported")]
        public override Task Updating_then_deleting_the_same_entity_results_in_DbUpdateConcurrencyException() { return Task.CompletedTask; }
        [Fact(Skip = "Unsupported by JET: rowversion unsupported")]
        public override Task Updating_then_deleting_the_same_entity_results_in_DbUpdateConcurrencyException_which_can_be_resolved_with_store_values() { return Task.CompletedTask; }

    }
}
