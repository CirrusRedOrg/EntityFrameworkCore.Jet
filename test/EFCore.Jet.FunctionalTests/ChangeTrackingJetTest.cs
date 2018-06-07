using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFramework.Jet.FunctionalTests
{
    public class ChangeTrackingJetTest : ChangeTrackingTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public ChangeTrackingJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture)
            : base(fixture)
        {
        }
    }
}


