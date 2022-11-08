using System;
using System.Data;
using System.Data.Common;
using EntityFrameworkCore.Jet.Data;
using System.Data.Odbc;
using System.Diagnostics;
using System.IO;

namespace EntityFrameworkCore.Jet.IntegrationTests
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
            return System.AppDomain.CurrentDomain.BaseDirectory;
        }

        public static string GetJetStorePath(string storeName = null)
        {
            return Path.Combine(GetTestDirectory(), storeName ?? GetStoreNameFromCallStack());
        }

        public static DbConnection GetJetConnection(string storeName = null)
            => new JetConnection(JetConnection.GetConnectionString(GetJetStorePath(storeName ?? GetStoreNameFromCallStack()), Helpers.DataAccessProviderFactory), Helpers.DataAccessProviderFactory);

        private static string GetStoreNameFromCallStack(int frames = 1)
        {
            var callerClassName = new StackTrace()
                .GetFrame(frames + 1)
                .GetMethod()
                .ReflectedType
                .Name;
            return callerClassName + ".accdb";
        }

        public static JetConnection CreateAndOpenJetDatabase(string storeName = null)
        {
            var connection = new JetConnection(CreateJetDatabase(storeName));
            connection.Open();
            return connection;
        }

        public static string CreateJetDatabase(string storeName = null)
        {
            storeName ??= GetStoreNameFromCallStack();
                
            DeleteJetDatabase(storeName);
            JetConnection.CreateDatabase(storeName);
            
            return storeName;
        }

        public static void DeleteJetDatabase(string storeName = null)
        {
            JetConnection.ClearAllPools();
            JetConnection.DropDatabase(storeName ?? GetStoreNameFromCallStack());
        }
 
        public static DbProviderFactory DataAccessProviderFactory { get; set; } = OdbcFactory.Instance;
    }
}