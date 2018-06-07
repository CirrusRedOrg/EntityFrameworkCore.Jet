using System;
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

        private static string GetTestDirectory()
        {
            return System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase.Replace("file:///", ""));
        }

        public static DbConnection GetJetConnection()
        {
            // Take care because according to this article
            // http://msdn.microsoft.com/en-us/library/dd0w4a2z(v=vs.110).aspx
            // to make the following line work the provider must be installed in the GAC and we also need an entry in machine.config
            /*
            DbProviderFactory providerFactory = System.Data.Common.DbProviderFactories.GetFactory("JetEntityFrameworkProvider");
            
            DbConnection connection = providerFactory.CreateConnection();
            */

            DbConnection connection = new JetConnection();

            connection.ConnectionString = GetJetConnectionString();
            return connection;

        }

        public static string GetJetConnectionString()
        {
            // ReSharper disable once CollectionNeverUpdated.Local
            OleDbConnectionStringBuilder oleDbConnectionStringBuilder = new OleDbConnectionStringBuilder();
            //oleDbConnectionStringBuilder.Provider = "Microsoft.Jet.OLEDB.4.0";
            //oleDbConnectionStringBuilder.DataSource = @".\Empty.mdb";
            //oleDbConnectionStringBuilder.Provider = "Microsoft.ACE.OLEDB.12.0";
            oleDbConnectionStringBuilder.Provider = "Microsoft.ACE.OLEDB.15.0";
            oleDbConnectionStringBuilder.DataSource = GetTestDirectory() + "\\Empty.accdb";
            return oleDbConnectionStringBuilder.ToString();
        }


        public static DbConnection GetSqlServerConnection()
        {
            DbConnection connection = new System.Data.SqlClient.SqlConnection("Data Source=(local);Initial Catalog=JetEfProviderComparativeTest;Integrated Security=true");
            return connection;
        }


        public static string[] GetQueries(string s)
        {
            string query = string.Empty;
            List<string> queries = new List<string>();

            foreach (string line in s.Replace("\r\n", "\n").Split('\n'))
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
                int i = queryAndParameters.IndexOf("\n-\n", StringComparison.Ordinal);
                query = queryAndParameters.Substring(0, i);
                parameterString = queryAndParameters.Substring(i + 3);
            }
            else
            {
                query = queryAndParameters;
                parameterString = null;
            }

            string[] sqlParts = query.Split('\n');
            string executionMethod = sqlParts[0];
            string sql = string.Empty;
            for (int i = 1; i < sqlParts.Length; i++)
                sql += sqlParts[i] + "\r\n";

            var command = connection.CreateCommand();
            if (transaction != null)
                command.Transaction = transaction;
            command.CommandText = sql;

            if (parameterString != null)
            {
                string[] parameterStringList = parameterString.Split('\n');
                foreach (string sParameter in parameterStringList)
                {
                    if (string.IsNullOrWhiteSpace(sParameter))
                        continue;
                    Match match = _parseParameterRegex.Match(sParameter);
                    if (!match.Success)
                        throw new Exception("Parameter not valid " + sParameter);
                    string parameterName = match.Groups["name"].Value;
                    string parameterType = match.Groups["type"].Value;
                    string sparameterValue = match.Groups["value"].Value;
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
    }
}
