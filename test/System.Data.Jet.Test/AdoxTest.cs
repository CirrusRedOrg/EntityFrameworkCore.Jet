using System;
using System.Data.Common;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.Test
{
    [TestClass]
    public class AdoxTest
    {
        [TestMethod]
        public void CreateDatabase()
        {
            try
            {
                JetConnection.ClearAllPools();
                File.Delete("AdoxTest.accdb");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            AdoxWrapper.CreateEmptyDatabase(JetConnection.GetConnectionString("AdoxTest.accdb"));

            JetConnection.ClearAllPools();
            File.Delete("AdoxTest.accdb");
        }

        [TestMethod]
        public void RenameTableAdox()
        {
            try
            {
                JetConnection.ClearAllPools();
                File.Delete("AdoxTest.accdb");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            AdoxWrapper.CreateEmptyDatabase(JetConnection.GetConnectionString("AdoxTest.accdb"));

            using (JetConnection connection = new JetConnection(JetConnection.GetConnectionString("AdoxTest.accdb")))
            {
                connection.Open();
                CreateTable(connection);

                CheckTableExists(connection, "tableName");

                AdoxWrapper.RenameTable(JetConnection.GetConnectionString("AdoxTest.accdb"), "tableName", "newTableName");

                CheckTableExists(connection, "newTableName");
            }

            JetConnection.ClearAllPools();
            File.Delete("AdoxTest.accdb");

        }



        [TestMethod]
        public void RenameColumnAdox()
        {
            try
            {
                JetConnection.ClearAllPools();
                File.Delete("AdoxTest.accdb");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            AdoxWrapper.CreateEmptyDatabase(JetConnection.GetConnectionString("AdoxTest.accdb"));

            using (JetConnection connection = new JetConnection(JetConnection.GetConnectionString("AdoxTest.accdb")))
            {
                connection.Open();
                CreateTable(connection);

                CheckColumnExists(connection, "tableName", "columnName");

                AdoxWrapper.RenameColumn(JetConnection.GetConnectionString("AdoxTest.accdb"), "tableName", "columnName", "newColumnName");

                connection.Close();
                JetConnection.ClearAllPools();
                connection.Open();
                CheckColumnExists(connection, "tableName", "newColumnName");
                
            }

            JetConnection.ClearAllPools();
            File.Delete("AdoxTest.accdb");

        }


        //[TestMethod]
        public void RenameIndexAdox()
        {
            try
            {
                JetConnection.ClearAllPools();
                File.Delete("AdoxTest.accdb");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            AdoxWrapper.CreateEmptyDatabase(JetConnection.GetConnectionString("AdoxTest.accdb"));

            using (JetConnection connection = new JetConnection(JetConnection.GetConnectionString("AdoxTest.accdb")))
            {
                connection.Open();
                CreateTable(connection);

                CheckIndexExists(connection, "indexName");

                AdoxWrapper.RenameIndex(JetConnection.GetConnectionString("AdoxTest.accdb"), "tableName", "indexName", "newIndexName");

                connection.Close();
                connection.Open();
                CheckIndexExists(connection, "newIndexName");

            }

            JetConnection.ClearAllPools();
            File.Delete("AdoxTest.accdb");
        }


        [TestMethod]
        public void RenameTableQuery()
        {
            try
            {
                JetConnection.ClearAllPools();
                File.Delete("AdoxTest.accdb");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            AdoxWrapper.CreateEmptyDatabase(JetConnection.GetConnectionString("AdoxTest.accdb"));

            using (JetConnection connection = new JetConnection(JetConnection.GetConnectionString("AdoxTest.accdb")))
            {
                connection.Open();
                CreateTable(connection);

                CheckTableExists(connection, "tableName");

                connection.CreateCommand("rename table tableName to newTableName").ExecuteNonQuery();

                CheckTableExists(connection, "newTableName");
            }

            JetConnection.ClearAllPools();

            File.Delete("AdoxTest.accdb");

        }



        [TestMethod]
        public void RenameColumnQuery()
        {
            try
            {
                JetConnection.ClearAllPools();
                File.Delete("AdoxTest.accdb");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            AdoxWrapper.CreateEmptyDatabase(JetConnection.GetConnectionString("AdoxTest.accdb"));

            using (JetConnection connection = new JetConnection(JetConnection.GetConnectionString("AdoxTest.accdb")))
            {
                connection.Open();
                CreateTable(connection);

                CheckColumnExists(connection, "tableName", "columnName");

                connection.CreateCommand("rename column tableName.columnName to newColumnName").ExecuteNonQuery();

                connection.Close();
                JetConnection.ClearAllPools();
                connection.Open();
                CheckColumnExists(connection, "tableName", "newColumnName");

            }

            JetConnection.ClearAllPools();
            File.Delete("AdoxTest.accdb");

        }



        private void CheckTableExists(JetConnection connection, string tableName)
        {
            var command = connection.CreateCommand($"SELECT COUNT(*) FROM (SHOW TABLES) WHERE Name='{tableName}'");
            int result = (int)command.ExecuteScalar();
            command.Dispose();
            if (result != 1)
                throw new Exception($"Table {tableName} not found");
        }

        private void CheckColumnExists(JetConnection connection, string tableName, string columnName)
        {
            var command = connection.CreateCommand($"SELECT COUNT(*) FROM (SHOW TABLECOLUMNS) WHERE Table='{tableName}' AND Name='{columnName}'");
            int result = (int)command.ExecuteScalar();
            command.Dispose();
            if (result != 1)
                throw new Exception($"Column {tableName}.{columnName} not found");
        }

        private void CheckIndexExists(JetConnection connection, string indexName)
        {
            var command = connection.CreateCommand($"SELECT COUNT(*) FROM (SHOW INDEXES) WHERE Name='{indexName}'");
            int result = (int)command.ExecuteScalar();
            command.Dispose();
            if (result != 1)
                throw new Exception($"Index {indexName} not found");
        }



        private static void CreateTable(JetConnection connection)
        {
            DbCommand command;
            command = connection.CreateCommand(@"
CREATE TABLE tableName (columnName int);
CREATE INDEX indexName ON tableName (columnName);");
            command.ExecuteNonQuery();
            command.Dispose();
        }
    }
}
