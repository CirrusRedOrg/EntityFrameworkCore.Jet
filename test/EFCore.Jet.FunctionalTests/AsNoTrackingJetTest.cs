using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFramework.Jet.FunctionalTests
{
    public class AsNoTrackingJetTest : AsNoTrackingTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public AsNoTrackingJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture)
            : base(fixture)
        {
        }
    }
}
