using EntityFramework.Jet.FunctionalTests;

namespace EntityFrameworkCore.Jet.Design.FunctionalTests.ReverseEngineering
{
    public class JetE2EFixture
    {
        public JetE2EFixture()
        {
            JetTestStore.GetOrCreateShared("E2E", () => { });
        }
    }
}
