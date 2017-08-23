using System;
using System.Data.Common;
using System.Data.Jet;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test
{
    [TestClass]
    public class AssemblyInitialization
    {

        public static DbConnection Connection;

        [AssemblyInitialize]
        static public void AssemblyInitialize(TestContext testContext)
        {

            // This is the only reason why we include the Provider
            JetConfiguration.ShowSqlStatements = true;

            Connection = Helpers.GetJetConnection();

            Helpers.DeleteSqlCeDatabase();
            Helpers.CreateSqlCeDatabase();
        }


        [AssemblyCleanup]
        static public void AssemblyCleanup()
        {
            if (Connection != null)
                Connection.Dispose();

            Helpers.DeleteSqlCeDatabase();
        }


    }
}
