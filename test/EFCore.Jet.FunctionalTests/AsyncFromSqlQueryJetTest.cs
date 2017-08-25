using Microsoft.EntityFrameworkCore.Query;

namespace EntityFramework.Jet.FunctionalTests
{
    public class AsyncFromSqlQueryJetTest : AsyncFromSqlQueryTestBase<NorthwindQueryJetFixture>
    {
        public AsyncFromSqlQueryJetTest(NorthwindQueryJetFixture fixture)
            : base(fixture)
        {
        }
    }
}
