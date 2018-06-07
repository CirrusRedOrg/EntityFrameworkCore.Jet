using Microsoft.EntityFrameworkCore;

namespace EntityFramework.Jet.FunctionalTests
{
    public class DatabindingJetTest : DatabindingTestBase<F1JetFixture>
    {
        public DatabindingJetTest(F1JetFixture fixture)
            : base(fixture)
        {
        }
    }
}