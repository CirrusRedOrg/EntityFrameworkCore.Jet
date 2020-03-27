using System.Collections.Generic;
using System.Data.Common;
using System.Data.OleDb;
using System.Text.RegularExpressions;

namespace System.Data.Jet.Test
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

        public static void ShowDataReaderContent(DbConnection dbConnection, string sqlStatement)
        {
            var command = dbConnection.CreateCommand();
            command.CommandText = sqlStatement;
            var dataReader = command.ExecuteReader();

            var first = true;

            for (var i = 0; i < dataReader.FieldCount; i++)
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
                for (var i = 0; i < dataReader.FieldCount; i++)
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
            return System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly()
                    .GetName()
                    .CodeBase.Replace("file:///", ""));
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

            return queries.ToArray();
        }

        public static DbDataReader Execute(DbConnection connection, string query)
        {
            return InternatExecute(connection, null, query);
        }

        public static DbDataReader Execute(DbConnection connection, DbTransaction transaction, string query)
        {
            return InternatExecute(connection, transaction, query);
        }

        private static Regex _parseParameterRegex = new Regex(@"(?<name>.*)\((?<type>.*)\) = (?<value>.*)", RegexOptions.IgnoreCase);

        private static DbDataReader InternatExecute(DbConnection connection, DbTransaction transaction, string queryAndParameters)
        {
            string query;
            string parameterString;

            if (queryAndParameters.Contains("\n-\n"))
            {
                var i = queryAndParameters.IndexOf("\n-\n", StringComparison.Ordinal);
                query = queryAndParameters.Substring(0, i);
                parameterString = queryAndParameters.Substring(i + 3);
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
                    command.Parameters.Add(new OleDbParameter(parameterName, parameterValue));
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

        public static JetConnection CreateAndOpenDatabase(string storeName)
        {
            var connection = new JetConnection(CreateDatabase(storeName));
            connection.Open();
            return connection;
        }

        public static string CreateDatabase(string storeName)
        {
            DeleteDatabase(storeName);

            var connectionString = JetConnection.GetConnectionString(storeName);
            AdoxWrapper.CreateEmptyDatabase(connectionString);
            return connectionString;
        }

        public static void DeleteDatabase(string storeName)
        {
            JetConnection.ClearAllPools();
            JetConnection.DropDatabase(JetConnection.GetConnectionString(storeName));
        }
    }
}