// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

#pragma warning disable IDE0022 // Use block body for methods
// ReSharper disable SuggestBaseTypeForParameter
namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public class JetTestStore : RelationalTestStore
    {
        public const int CommandTimeout = 300;

        public static JetTestStore GetNorthwindStore()
            => (JetTestStore)JetNorthwindTestStoreFactory.Instance
                .GetOrCreate(JetNorthwindTestStoreFactory.Name)
                .Initialize(null, (Func<DbContext>)null);

        public static JetTestStore GetOrCreate(string name, string scriptPath = null, string templatePath = null)
            => new JetTestStore(name, scriptPath: scriptPath, templatePath: templatePath);

        public static JetTestStore GetOrCreateInitialized(string name)
            => new JetTestStore(name).InitializeJet(null, (Func<DbContext>)null, null);

        public static JetTestStore Create(string name)
            => new JetTestStore(name, shared: false);

        public static JetTestStore CreateInitialized(string name)
            => new JetTestStore(name, shared: false)
                .InitializeJet(null, (Func<DbContext>)null, null);

        private readonly string _scriptPath;
        private readonly string _templatePath;

        private JetTestStore(
            string name,
            string scriptPath = null,
            string templatePath = null,
            bool shared = true)
            : base(name + ".accdb", shared)
        {
            if (scriptPath != null)
            {
                _scriptPath = Path.Combine(
                    Path.GetDirectoryName(
                        typeof(JetTestStore).GetTypeInfo()
                            .Assembly.Location), scriptPath);
            }

            if (templatePath != null)
            {
                _templatePath = Path.Combine(
                    Path.GetDirectoryName(
                        typeof(JetTestStore).GetTypeInfo()
                            .Assembly.Location), templatePath);
            }

            ConnectionString = CreateConnectionString(Name);

            var dataAccessProviderFactory = JetFactory.Instance.GetDataAccessProviderFactory(JetConnection.GetDataAccessProviderType(ConnectionString));
            var connection = (JetConnection)JetFactory.Instance.CreateConnection();
            connection.ConnectionString = ConnectionString;
            connection.DataAccessProviderFactory = dataAccessProviderFactory;

            Connection = connection;
        }

        public JetTestStore InitializeJet(
            IServiceProvider serviceProvider, Func<DbContext> createContext, Action<DbContext> seed)
            => (JetTestStore)Initialize(serviceProvider, createContext, seed);

        public JetTestStore InitializeJet(
            IServiceProvider serviceProvider, Func<JetTestStore, DbContext> createContext, Action<DbContext> seed)
            => InitializeJet(serviceProvider, () => createContext(this), seed);

        protected override void Initialize(Func<DbContext> createContext, Action<DbContext> seed, Action<DbContext> clean)
        {
            if (CreateDatabase(clean))
            {
                if (_scriptPath != null)
                {
                    ExecuteScript(_scriptPath);
                }
                else if (_templatePath == null)
                {
                    using (var context = createContext())
                    {
                        context.Database.EnsureCreatedResiliently();
                        seed?.Invoke(context);
                    }
                }
            }
        }

        public override DbContextOptionsBuilder AddProviderOptions(DbContextOptionsBuilder builder)
            => builder.UseJet(Connection, b => b.ApplyConfiguration().UseShortTextForSystemString()).EnableSensitiveDataLogging().EnableDetailedErrors();

        private bool CreateDatabase(Action<DbContext> clean)
        {
            var connectionString = CreateConnectionString(Name);

            if (JetConnection.DatabaseExists(connectionString))
            {
                // Only reseed scripted databases during CI runs
                if (_scriptPath != null &&
                    _templatePath == null &&
                    !TestEnvironment.IsCI)
                {
                    //return false;
                }

                // Delete the database to ensure it's recreated with the correct file path
                DeleteDatabase();
            }

            if (_templatePath != null)
            {
                File.Copy(_templatePath, Name);
            }
            else
            {
                JetConnection.CreateDatabase(connectionString);
            }

            return true;
        }

        public override void Clean(DbContext context)
            => context.Database.EnsureClean();

        public void ExecuteScript(string scriptPath)
        {
            var script = File.ReadAllText(scriptPath);
            var batches = new Regex(@"\s*;\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromMilliseconds(1000.0))
                .Split(script)
                .Where(b => !string.IsNullOrEmpty(b))
                .ToList();

            ExecuteBatch(
                Connection,
                false,
                batches,
                (command, batch) =>
                {
                    command.CommandText = batch;
                    command.ExecuteNonQuery();
                });
        }

        public void DeleteDatabase()
        {
            if (ConnectionState != ConnectionState.Closed)
            {
                CloseConnection();
            }
            JetConnection.DropDatabase(CreateConnectionString(Name));
        }

        public override void OpenConnection()
            => new TestJetRetryingExecutionStrategy().Execute(Connection, connection => connection.Open());

        public override Task OpenConnectionAsync()
            => new TestJetRetryingExecutionStrategy().ExecuteAsync(Connection, connection => connection.OpenAsync());

        public T ExecuteScalar<T>(string sql, params object[] parameters)
            => ExecuteScalar<T>(Connection, sql, parameters);

        private static T ExecuteScalar<T>(DbConnection connection, string sql, params object[] parameters)
            => Execute(connection, command => (T)command.ExecuteScalar(), sql, false, parameters);

        public Task<T> ExecuteScalarAsync<T>(string sql, params object[] parameters)
            => ExecuteScalarAsync<T>(Connection, sql, parameters);

        private static Task<T> ExecuteScalarAsync<T>(DbConnection connection, string sql, IReadOnlyList<object> parameters = null)
            => ExecuteAsync(connection, async command => (T)await command.ExecuteScalarAsync(), sql, false, parameters);

        public int ExecuteNonQuery(string sql, params object[] parameters)
            => ExecuteNonQuery(Connection, sql, parameters);

        private static int ExecuteNonQuery(DbConnection connection, string sql, object[] parameters = null)
            => Execute(connection, command => command.ExecuteNonQuery(), sql, false, parameters);

        public Task<int> ExecuteNonQueryAsync(string sql, params object[] parameters)
            => ExecuteNonQueryAsync(Connection, sql, parameters);

        private static Task<int> ExecuteNonQueryAsync(DbConnection connection, string sql, IReadOnlyList<object> parameters = null)
            => ExecuteAsync(connection, command => command.ExecuteNonQueryAsync(), sql, false, parameters);

        public IEnumerable<T> Query<T>(string sql, params object[] parameters)
            => Query<T>(Connection, sql, parameters);

        private static IEnumerable<T> Query<T>(DbConnection connection, string sql, object[] parameters = null)
            => Execute(
                connection, command =>
                {
                    using (var dataReader = command.ExecuteReader())
                    {
                        var results = Enumerable.Empty<T>();
                        while (dataReader.Read())
                        {
                            results = results.Concat(new[] { dataReader.GetFieldValue<T>(0) });
                        }

                        return results;
                    }
                }, sql, false, parameters);

        public Task<IEnumerable<T>> QueryAsync<T>(string sql, params object[] parameters)
            => QueryAsync<T>(Connection, sql, parameters);

        private static Task<IEnumerable<T>> QueryAsync<T>(DbConnection connection, string sql, object[] parameters = null)
            => ExecuteAsync(
                connection, async command =>
                {
                    using (var dataReader = await command.ExecuteReaderAsync())
                    {
                        var results = Enumerable.Empty<T>();
                        while (await dataReader.ReadAsync())
                        {
                            results = results.Concat(new[] { await dataReader.GetFieldValueAsync<T>(0) });
                        }

                        return results;
                    }
                }, sql, false, parameters);

        private static T Execute<T>(
            DbConnection connection, Func<DbCommand, T> execute, string sql,
            bool useTransaction = false, object[] parameters = null)
            => new TestJetRetryingExecutionStrategy().Execute(
                new
                {
                    connection,
                    execute,
                    sql,
                    useTransaction,
                    parameters
                },
                state => ExecuteCommand(state.connection, state.execute, state.sql, state.useTransaction, state.parameters));

        private static void ExecuteBatch<T>(
            DbConnection connection, bool useTransaction, IEnumerable<T> items, Action<DbCommand, T> execute)
        {
            if (connection.State != ConnectionState.Closed)
            {
                connection.Close();
            }

            connection.Open();
            try
            {
                using (var transaction = useTransaction
                    ? connection.BeginTransaction()
                    : null)
                {
                    foreach (var item in items)
                    {
                        new TestJetRetryingExecutionStrategy().Execute(
                            () =>
                            {
                                using (var command = CreateCommand(connection))
                                {
                                    command.Transaction = transaction;
                                    execute(command, item);
                                }
                            });
                    }

                    transaction?.Commit();
                }
            }
            finally
            {
                if (connection.State != ConnectionState.Closed)
                {
                    connection.Close();
                }
            }
        }

        private static T ExecuteCommand<T>(
            DbConnection connection, Func<DbCommand, T> execute, string sql, bool useTransaction, object[] parameters)
        {
            if (connection.State != ConnectionState.Closed)
            {
                connection.Close();
            }

            connection.Open();
            try
            {
                using (var transaction = useTransaction
                    ? connection.BeginTransaction()
                    : null)
                {
                    T result;
                    using (var command = CreateCommand(connection, sql, parameters))
                    {
                        command.Transaction = transaction;
                        result = execute(command);
                    }

                    transaction?.Commit();

                    return result;
                }
            }
            finally
            {
                if (connection.State != ConnectionState.Closed)
                {
                    connection.Close();
                }
            }
        }

        private static Task<T> ExecuteAsync<T>(
            DbConnection connection, Func<DbCommand, Task<T>> executeAsync, string sql,
            bool useTransaction = false, IReadOnlyList<object> parameters = null)
            => new TestJetRetryingExecutionStrategy().ExecuteAsync(
                new
                {
                    connection,
                    executeAsync,
                    sql,
                    useTransaction,
                    parameters
                },
                state => ExecuteCommandAsync(state.connection, state.executeAsync, state.sql, state.useTransaction, state.parameters));

        private static async Task<T> ExecuteCommandAsync<T>(
            DbConnection connection, Func<DbCommand, Task<T>> executeAsync, string sql, bool useTransaction,
            IReadOnlyList<object> parameters)
        {
            if (connection.State != ConnectionState.Closed)
            {
                await connection.CloseAsync();
            }

            await connection.OpenAsync();
            try
            {
                using (var transaction = useTransaction
                    ? await connection.BeginTransactionAsync()
                    : null)
                {
                    T result;
                    using (var command = CreateCommand(connection, sql, parameters))
                    {
                        result = await executeAsync(command);
                    }

                    if (transaction != null)
                    {
                        await transaction.CommitAsync();
                    }

                    return result;
                }
            }
            finally
            {
                if (connection.State != ConnectionState.Closed)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private static DbCommand CreateCommand(
            DbConnection connection, string commandText = null, IReadOnlyList<object> parameters = null)
        {
            var command = (JetCommand)connection.CreateCommand();

            command.CommandText = commandText;
            command.CommandTimeout = CommandTimeout;

            if (parameters != null)
            {
                for (var i = 0; i < parameters.Count; i++)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "p" + i;
                    parameter.Value = parameters[i];

                    command.Parameters.Add(parameter);
                }
            }

            return command;
        }

        public override void Dispose()
        {
            base.Dispose();

            // Clean up the database using a local file, as it might get deleted later

            // Keep local file for debugging purposes.
            // DeleteDatabase();
        }

        public static string CreateConnectionString(string name)
        {
            var defaultConnectionString = TestEnvironment.DefaultConnection;
            var dataAccessProviderFactory = JetFactory.Instance.GetDataAccessProviderFactory(JetConnection.GetDataAccessProviderType(defaultConnectionString));
            var connectionStringBuilder = dataAccessProviderFactory.CreateConnectionStringBuilder();

            connectionStringBuilder.ConnectionString = defaultConnectionString;
            connectionStringBuilder.SetDataSource(name);

            return connectionStringBuilder.ToString();
        }

        public override string NormalizeDelimitersInRawString(string sql)
            => sql.Replace("[", "`").Replace("]", "`");

        public bool IsOleDb()
        {
            return ((EntityFrameworkCore.Jet.Data.JetConnection)Connection).DataAccessProviderFactory is OleDbFactory;
        }
    }
}