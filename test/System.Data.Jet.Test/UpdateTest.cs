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
            var queries = Helpers.GetQueries(Properties.Resources.UpdateTestQueries);
            Assert.AreEqual(6, queries.Length);

            using (var connection = Helpers.GetJetConnection())
            {
                connection.Open();
                DbDataReader reader;
                for (int index = 0; index < queries.Length - 2; index++)
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

                Helpers.Execute(connection, queries[5]);
                

            }
        }


        [TestMethod]
        public void UpdateTestWithTransactionsRun()
        {
            JetConfiguration.ShowSqlStatements = true;

            var queries = Helpers.GetQueries(Properties.Resources.UpdateTestQueries);
            Assert.AreEqual(6, queries.Length);

            using (var connection = Helpers.GetJetConnection())
            {
                connection.Open();
                DbDataReader reader;
                for (int index = 0; index < queries.Length - 2; index++)
                {
                    DbTransaction transaction = connection.BeginTransaction();
                    string query = queries[index];
                    reader = Helpers.Execute(connection, transaction, query);
                    if (reader != null)
                        reader.Dispose();
                    transaction.Commit();
                }
                reader = Helpers.Execute(connection, queries[4]);
                reader.Read();
                Assert.AreEqual(1, reader.GetInt32(0));
                reader.Dispose();

                Helpers.Execute(connection, queries[5]);


            }
        }


    }
}
