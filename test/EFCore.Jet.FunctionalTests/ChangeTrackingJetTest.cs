using Microsoft.EntityFrameworkCore.Query;

namespace EntityFramework.Jet.FunctionalTests
{
    public class ChangeTrackingJetTest : ChangeTrackingTestBase<NorthwindQueryJetFixture>
    {
        public ChangeTrackingJetTest(NorthwindQueryJetFixture fixture)
            : base(fixture)
        {
        }
    }
}


