using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.Test
{
    [TestClass]
    public class InsertTest
    {
        [TestMethod]
        public void InsertTestRun()
        {
            var queries = Helpers.GetQueries(Properties.Resources.InsertTestQueries);
            Assert.AreEqual(4, queries.Length);

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


                Helpers.Execute(connection, queries[3]);
                

            }
        }

    }
}
