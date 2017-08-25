using Microsoft.EntityFrameworkCore;

namespace EntityFramework.Jet.FunctionalTests
{
    public class JetServiceCollectionExtensionsTest : EntityFrameworkServiceCollectionExtensionsTest
    {
        public JetServiceCollectionExtensionsTest()
            : base(JetTestHelpers.Instance)
        {
        }
    }
}