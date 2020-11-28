using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    [TestClass]
    public class AssemblyInitialization
    {
        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext testContext)
        {
            // This is the only reason why we include the Provider
            // JetConfiguration.ShowSqlStatements = true;
            JetConfiguration.UseConnectionPooling = false;
        }
    }
}
