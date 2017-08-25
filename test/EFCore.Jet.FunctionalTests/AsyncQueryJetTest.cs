using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels;
using Xunit;

#pragma warning disable 1998

namespace EntityFramework.Jet.FunctionalTests
{
    public class AsyncQueryJetTest : AsyncQueryTestBase<NorthwindQueryJetFixture>
    {
        [Fact(Skip = "Investigate - https://github.com/aspnet/EntityFramework/issues/9378")]
        public override Task Where_subquery_on_collection()
        {
            return base.Where_subquery_on_collection();
        }

        [Fact(Skip = "SQL CE limitation")]
        public override async Task Sum_over_subquery_is_client_eval()
        {
            //return base.Sum_over_subquery_is_client_eval();
        }

        [Fact(Skip = "SQL CE limitation")]
        public override async Task Min_over_subquery_is_client_eval()
        {
            //return base.Min_over_subquery_is_client_eval();
        }

        [Fact(Skip = "SQL CE limitation")]
        public override async Task Max_over_subquery_is_client_eval()
        {
            //return base.Max_over_subquery_is_client_eval();
        }

        [Fact(Skip = "SQL CE limitation")]
        public override async Task Average_over_subquery_is_client_eval()
        {
            //return base.Average_over_subquery_is_client_eval();
        }

        [Fact(Skip = "SQL CE limitation")]
        public override async Task OrderBy_correlated_subquery_lol()
        {
            //return base.OrderBy_correlated_subquery_lol();
        }

        [Fact(Skip = "SQL CE limitation")]
        public override async Task SelectMany_primitive_select_subquery()
        {
            //return base.SelectMany_primitive_select_subquery();
        }

        [Fact(Skip = "Investigate 2.1 - https://github.com/aspnet/EntityFramework/issues/9369")]
        public override async Task String_Contains_Literal()
        {
            await AssertQuery<ChangedChangingMonsterContext.Customer>(
                cs => cs.Where(c => c.Name.Contains("M")), // case-insensitive
                cs => cs.Where(c => c.Name.Contains("M")
                                     || c.Name.Contains("m")), // case-sensitive
                entryCount: 34);
        }

        [Fact(Skip = "Investigate 2.1 - https://github.com/aspnet/EntityFramework/issues/9369")]
        public override async Task String_Contains_MethodCall()
        {
            await AssertQuery<ChangedChangingMonsterContext.Customer>(
                cs => cs.Where(c => c.Name.Contains(LocalMethod1())), // case-insensitive
                cs => cs.Where(c => c.Name.Contains(LocalMethod1().ToLower()) || c.Name.Contains(LocalMethod1().ToUpper())), // case-sensitive
                entryCount: 34);
        }

        [Fact]
        public async Task Single_Predicate_Cancellation()
        {
            await Assert.ThrowsAsync<TaskCanceledException>(
                async () =>
                    await Single_Predicate_Cancellation_test(Fixture.TestSqlLoggerFactory.CancelQuery()));
        }

        public AsyncQueryJetTest(NorthwindQueryJetFixture fixture)
            : base(fixture)
        {
        }
    }
}
