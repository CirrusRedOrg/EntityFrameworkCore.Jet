using System.Data.Common;
using EntityFrameworkCore.Jet.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    [TestClass]
    public class AssemblyInitialization
    {
        //public static DbConnection Connection;

        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext testContext)
        {
            // This is the only reason why we include the Provider
            JetConfiguration.ShowSqlStatements = true;
            JetConfiguration.UseConnectionPooling = false;

            //Connection = Helpers.CreateAndOpenJetDatabase(Helpers.DefaultJetStoreName);
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            //Connection?.Dispose();
            //Helpers.DeleteJetDatabase(Helpers.DefaultJetStoreName);
        }
    }
}