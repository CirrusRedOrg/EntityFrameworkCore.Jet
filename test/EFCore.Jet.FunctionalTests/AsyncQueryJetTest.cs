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
