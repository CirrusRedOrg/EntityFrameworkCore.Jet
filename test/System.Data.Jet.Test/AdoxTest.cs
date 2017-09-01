using System;
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
                File.Delete("AdoxTest.accdb");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            AdoxWrapper.CreateEmptyDatabase(JetConnection.GetConnectionString("AdoxTest.accdb"));

            File.Delete("AdoxTest.accdb");
        }

        [TestMethod]
        public void RenameTableAdox()
        {
            try
            {
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

            File.Delete("AdoxTest.accdb");

        }



        [TestMethod]
        public void RenameColumnAdox()
        {
            try
            {
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
                connection.Open();
                CheckColumnExists(connection, "tableName", "newColumnName");
                
            }

            File.Delete("AdoxTest.accdb");

        }



        [TestMethod]
        public void RenameTableQuery()
        {
            try
            {
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

            File.Delete("AdoxTest.accdb");

        }



        [TestMethod]
        public void RenameColumnQuery()
        {
            try
            {
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
                connection.Open();
                CheckColumnExists(connection, "tableName", "newColumnName");

            }

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


        private static void CreateTable(JetConnection connection)
        {
            var command = connection.CreateCommand("CREATE TABLE tableName (columnName int)");
            command.ExecuteNonQuery();
            command.Dispose();
        }
    }
}
