using Microsoft.EntityFrameworkCore.Query;

namespace EntityFramework.Jet.FunctionalTests
{
    public class IncludeAsyncJetTest : IncludeAsyncTestBase<NorthwindQueryJetFixture>
    {
        public IncludeAsyncJetTest(NorthwindQueryJetFixture fixture)
            : base(fixture)
        {
        }
    }
}


