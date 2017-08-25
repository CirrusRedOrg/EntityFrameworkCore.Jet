using Microsoft.EntityFrameworkCore.Query;

namespace EntityFramework.Jet.FunctionalTests
{
    public class AsNoTrackingJetTest : AsNoTrackingTestBase<NorthwindQueryJetFixture>
    {
        public AsNoTrackingJetTest(NorthwindQueryJetFixture fixture)
            : base(fixture)
        {
        }
    }
}
