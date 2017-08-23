using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.Test
{
    [TestClass]
    public class UpdateTest
    {
        [TestMethod]
        public void UpdateTestRun()
        {
            JetConfiguration.ShowSqlStatements = true;

            var queries = Helpers.GetQueries(Properties.Resources.UpdateTestQueries);
            Assert.AreEqual(5, queries.Length);

            using (var connection = Helpers.GetJetConnection())
            {
                connection.Open();
                DbDataReader reader;
                for (int index = 0; index < queries.Length - 1; index++)
                {
                    string query = queries[index];
                    reader = Helpers.Execute(connection, query);
                    if (reader != null)
                        reader.Dispose();
                }
                reader = Helpers.Execute(connection, queries[4]);
                reader.Read();
                Assert.AreEqual(1, reader.GetInt32(0));
                reader.Dispose();

            }
        }
    }
}
