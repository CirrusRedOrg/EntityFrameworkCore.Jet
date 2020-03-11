using System;
using System.Data;
using System.Data.Common;
using System.Data.Jet;
using System.Data.OleDb;
using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;

namespace EFCore.Jet.Integration.Test
{
    static class Helpers
    {
        public const string DefaultJetStoreName = "Jet.accdb";

        public static int CountRows(JetConnection jetConnection, string sqlStatement)
        {
            DbCommand command = jetConnection.CreateCommand(sqlStatement);
            DbDataReader dataReader = command.ExecuteReader();

            int count = 0;
            while (dataReader.Read())
                count++;

            return count;
        }

        public static void ShowDataReaderContent(DbConnection dbConnection, string sqlStatement)
        {
            DbCommand command = dbConnection.CreateCommand();
            command.CommandText = sqlStatement;
            DbDataReader dataReader = command.ExecuteReader();

            bool first = true;

            for (int i = 0; i < dataReader.FieldCount; i++)
            {
                if (first)
                    first = false;
                else
                    Console.Write("\t");

                Console.Write(dataReader.GetName(i));
            }

            Console.WriteLine();

            while (dataReader.Read())
            {
                first = true;
                for (int i = 0; i < dataReader.FieldCount; i++)
                {
                    if (first)
                        first = false;
                    else
                        Console.Write("\t");

                    Console.Write("{0}", dataReader.GetValue(i));
                }

                Console.WriteLine();
            }
        }

        public static void ShowDataTableContent(DataTable dataTable)
        {
            bool first = true;

            foreach (DataColumn column in dataTable.Columns)
            {
                if (first)
                    first = false;
                else
                    Console.Write("\t");

                Console.Write(column.ColumnName);
            }

            Console.WriteLine();

            foreach (DataRow row in dataTable.Rows)
            {
                first = true;
                foreach (DataColumn column in dataTable.Columns)
                {
                    if (first)
                        first = false;
                    else
                        Console.Write("\t");

                    Console.Write("{0}", row[column]);
                }

                Console.WriteLine();
            }
        }

        public static string GetTestDirectory()
        {
            return System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly()
                    .GetName()
                    .CodeBase.Replace("file:///", ""));
        }

        public static string GetJetStorePath(string storeName = null)
        {
            return Path.Combine(GetTestDirectory(), storeName ?? GetStoreNameFromCallStack());
        }

        public static DbConnection GetJetConnection(string storeName = null)
            => new JetConnection(JetConnection.GetConnectionString(GetJetStorePath(storeName ?? GetStoreNameFromCallStack())));

        private static string GetStoreNameFromCallStack(int frames = 1)
        {
            var callerClassName = new StackTrace()
                .GetFrame(frames + 1)
                .GetMethod()
                .ReflectedType
                .Name;
            return callerClassName + ".accdb";
        }

        public static DbConnection GetSqlServerConnection()
        {
#if NETFRAMEWORK
            DbConnection connection = new System.Data.SqlClient.SqlConnection("Data Source=(local);Initial Catalog=JetEfProviderComparativeTest;Integrated Security=true");
#else
            DbConnection connection = new Microsoft.Data.SqlClient.SqlConnection("Data Source=(local);Initial Catalog=JetEfProviderComparativeTest;Integrated Security=true");

#endif
            return connection;
        }

        public static DbConnection GetSqliteConnection()
        {
            DbConnection connection = new SqliteConnection(@"Data Source=SQLite.db;");
            return connection;
        }

        public static JetConnection CreateAndOpenJetDatabase(string storeName = null)
        {
            var connection = new JetConnection(CreateJetDatabase(storeName ?? GetStoreNameFromCallStack()));
            connection.Open();
            return connection;
        }

        public static string CreateJetDatabase(string storeName = null)
        {
            DeleteJetDatabase(storeName);

            var connectionString = JetConnection.GetConnectionString(storeName ?? GetStoreNameFromCallStack());
            AdoxWrapper.CreateEmptyDatabase(connectionString);
            return connectionString;
        }

        public static void DeleteJetDatabase(string storeName = null)
        {
            JetConnection.ClearAllPools();
            JetConnection.DropDatabase(JetConnection.GetConnectionString(storeName ?? GetStoreNameFromCallStack()));
        }
    }
}