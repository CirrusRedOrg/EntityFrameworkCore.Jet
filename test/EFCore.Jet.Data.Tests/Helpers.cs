using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Odbc;
using System.Data.OleDb;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    static class Helpers
    {
        public static int CountRows(JetConnection jetConnection, string sqlStatement)
        {
            var command = jetConnection.CreateCommand(sqlStatement);
            var dataReader = command.ExecuteReader();

            var count = 0;
            while (dataReader.Read())
                count++;

            return count;
        }

        public static string GetDataReaderContent(DbConnection dbConnection, string sqlStatement)
        {
            const string delimiter = " | ";
            
            using var command = dbConnection.CreateCommand();
            command.CommandText = sqlStatement;
            
            using var dataReader = command.ExecuteReader();
            var content = new StringBuilder();

            for (var i = 0; i < dataReader.FieldCount; i++)
            {
                content.Append($"`{dataReader.GetName(i)}`");
                content.Append(delimiter);
            }
            content.Remove(content.Length - delimiter.Length, delimiter.Length);
            content.AppendLine();
            
            for (var i = 0; i < dataReader.FieldCount; i++)
            {
                content.Append("---");
                content.Append(delimiter);
            }
            content.Remove(content.Length - delimiter.Length, delimiter.Length);
            content.AppendLine();

            while (dataReader.Read())
            {
                for (var i = 0; i < dataReader.FieldCount; i++)
                {
                    content.Append(dataReader.GetValue(i));
                    content.Append(delimiter);
                }
                content.Remove(content.Length - delimiter.Length, delimiter.Length);
                content.AppendLine();
            }

            return content.ToString().TrimEnd();
        }

        public static void ShowDataTableContent(DataTable dataTable)
        {
            var first = true;

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

        private static string GetTestDirectory()
        {
            return System.AppDomain.CurrentDomain.BaseDirectory;
        }

        public static string[] GetQueries(string s)
        {
            var query = string.Empty;
            var queries = new List<string>();

            foreach (var line in s.Replace("\r\n", "\n")
                .Split('\n'))
            {
                if (line.Contains("======="))
                {
                    if (!string.IsNullOrWhiteSpace(query))
                        queries.Add(query);

                    query = string.Empty;
                }

                query += line + "\n";
            }

            if (!string.IsNullOrWhiteSpace(query))
                queries.Add(query);

            return [.. queries];
        }

        public static DbDataReader Execute(DbConnection connection, string query)
        {
            return InternatExecute(connection, null, query);
        }

        public static DbDataReader Execute(DbConnection connection, DbTransaction transaction, string query)
        {
            return InternatExecute(connection, transaction, query);
        }

        private static Regex _parseParameterRegex = new(@"(?<name>.*)\((?<type>.*)\) = (?<value>.*)", RegexOptions.IgnoreCase);

        private static DbDataReader InternatExecute(DbConnection connection, DbTransaction transaction, string queryAndParameters)
        {
            string query;
            string parameterString;

            if (queryAndParameters.Contains("\n-\n"))
            {
                var i = queryAndParameters.IndexOf("\n-\n", StringComparison.Ordinal);
                query = queryAndParameters[..i];
                parameterString = queryAndParameters[(i + 3)..];
            }
            else
            {
                query = queryAndParameters;
                parameterString = null;
            }

            var sqlParts = query.Split('\n');
            var executionMethod = sqlParts[0];
            var sql = string.Empty;
            for (var i = 1; i < sqlParts.Length; i++)
                sql += sqlParts[i] + "\r\n";

            var command = connection.CreateCommand();
            if (transaction != null)
                command.Transaction = transaction;
            command.CommandText = sql;

            if (parameterString != null)
            {
                var parameterStringList = parameterString.Split('\n');
                foreach (var sParameter in parameterStringList)
                {
                    if (string.IsNullOrWhiteSpace(sParameter))
                        continue;
                    var match = _parseParameterRegex.Match(sParameter);
                    if (!match.Success)
                        throw new Exception("Parameter not valid " + sParameter);
                    var parameterName = match.Groups["name"]
                        .Value;
                    var parameterType = match.Groups["type"]
                        .Value;
                    var sparameterValue = match.Groups["value"]
                        .Value;
                    object parameterValue;
                    if (sparameterValue == "null")
                        parameterValue = DBNull.Value;
                    else
                        parameterValue = Convert.ChangeType(sparameterValue, Type.GetType("System." + parameterType));

                    var parameter = command.CreateParameter();
                    parameter.ParameterName = parameterName;
                    parameter.Value = parameterValue;
                    
                    command.Parameters.Add(parameter);
                }
            }

            if (executionMethod.StartsWith("ExecuteNonQuery"))
            {
                command.ExecuteNonQuery();
                return null;
            }
            else if (executionMethod.StartsWith("ExecuteDbDataReader"))
            {
                return command.ExecuteReader();
            }
            else
                throw new Exception("Unknown execution method " + executionMethod);
        }

        public static void ExecuteScript(DbConnection connection, string script)
        {
            using var command = connection.CreateCommand();

            var batches = new Regex(@"\s*;\s*", RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromMilliseconds(1000.0))
                .Split(script)
                .Where(b => !string.IsNullOrEmpty(b))
                .ToList();

            var retryWaitTime = TimeSpan.FromMilliseconds(250);
            const int maxRetryCount = 6;
            var retryCount = 0;
            
            foreach (var batch in batches)
            {
                command.CommandText = batch;
                
                try
                {
                    command.ExecuteNonQuery();
                    retryCount = 0;
                }
                catch (Exception e)
                {
                    if (retryCount >= maxRetryCount)
                    {
                        Console.WriteLine(e.Message);
                        Console.WriteLine(batch);
                        throw;
                    }

                    retryCount++;
                    Thread.Sleep(retryWaitTime);
                }
            }
        }

        public static JetConnection CreateAndOpenDatabase(string storeName)
        {
            CreateDatabase(storeName);
            var connection = new JetConnection(storeName);
            connection.Open();
            return connection;
        }

        public static void CreateDatabase(
            string storeName,
            DatabaseVersion version = DatabaseVersion.NewestSupported,
            CollatingOrder collatingOrder = CollatingOrder.General,
            string databasePassword = null)
        {
            DeleteDatabase(storeName);
            JetConnection.CreateDatabase(storeName, version, collatingOrder, databasePassword);
        }

        public static void DeleteDatabase(string storeName)
        {
            JetConnection.ClearAllPools();
            JetConnection.DropDatabase(storeName);
        }

        public static JetConnection OpenDatabase(string storeName, DbProviderFactory dataAccessProviderFactory = null)
        {
            var connection = new JetConnection(storeName, dataAccessProviderFactory ?? DataAccessProviderFactory);

            try
            {
                connection.Open();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        public static JetConnection OpenDatabase(string storeName, DataAccessProviderType providerType)
            => OpenDatabase(storeName, JetFactory.GetDataAccessProviderFactory(providerType));

        public static DbProviderFactory DataAccessProviderFactory { get; set; } = OleDbFactory.Instance; //OdbcFactory.Instance;
    }
}