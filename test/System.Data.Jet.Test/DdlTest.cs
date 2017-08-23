using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.Test
{
    [TestClass]
    public class DdlTest
    {
        [TestMethod]
        public void CheckIfTablesExists()
        {

            var queries = Helpers.GetQueries(System.Data.Jet.Test.Properties.Resources.CheckIfTableExistsTestQueries);

            using (var connection = Helpers.GetJetConnection())
            {
                connection.Open();
                Helpers.Execute(connection, queries[0]);

                bool exists = ((JetConnection)AssemblyInitialization.Connection).TableExists("CheckIfTableExistsTable");
                Assert.IsTrue(exists);

                Helpers.Execute(connection, queries[1]);
            }

        }
    }
}
