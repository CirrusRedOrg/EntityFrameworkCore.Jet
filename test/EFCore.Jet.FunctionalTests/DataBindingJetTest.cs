using Microsoft.EntityFrameworkCore;

namespace EntityFramework.Jet.FunctionalTests
{
    public class DatabindingJetTest : DatabindingTestBase<JetTestStore, F1JetFixture>
    {
        public DatabindingJetTest(F1JetFixture fixture)
            : base(fixture)
        {
        }
    }
}