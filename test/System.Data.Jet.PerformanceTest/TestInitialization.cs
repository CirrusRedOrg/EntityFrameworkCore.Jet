using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.PerformanceTest
{
    //[TestClass]
    class TestInitialization
    {
        [AssemblyInitialize]
        static public void AssemblyInitialize(TestContext testContext)
        {
            JetDatabaseFixture.CreateAndSeedIfNotExists();
            // Enabling show sql statements can change performances
            //JetConfiguration.ShowSqlStatements = true;
        }

    }
}
