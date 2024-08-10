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
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using Microsoft.CodeAnalysis.Differencing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

#pragma warning disable IDE0022 // Use block body for methods
// ReSharper disable SuggestBaseTypeForParameter
namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public class JetTestStore : RelationalTestStore
    {
        public const int CommandTimeout = 300;

        private static string CurrentDirectory
            => Environment.CurrentDirectory;

        public static async Task<JetTestStore> GetNorthwindStoreAsync()
            => (JetTestStore)await JetNorthwindTestStoreFactory.Instance
                .GetOrCreate(JetNorthwindTestStoreFactory.Name).InitializeAsync(null, (Func<DbContext>?)null);

        public static JetTestStore GetOrCreate(string name)
            => new(name);

        public static async Task<JetTestStore> GetOrCreateInitializedAsync(string name)
            => await new JetTestStore(name).InitializeJetAsync(null, (Func<DbContext>?)null, null);

        public static JetTestStore GetOrCreateWithInitScript(string name, string initScript)
            => new(name, initScript: initScript);

        public static JetTestStore GetOrCreateWithScriptPath(
            string name,
            string scriptPath,
            bool shared = true)
            => new(name, scriptPath: scriptPath, shared: shared);

        public static JetTestStore Create(string name)
            => new(name, shared: false);

        public static async Task<JetTestStore> CreateInitializedAsync(
            string name,
            bool? multipleActiveResultSets = null)
            => await new JetTestStore(name, shared: false)
                .InitializeJetAsync(null, (Func<DbContext>?)null, null);

        private readonly string? _initScript;
        private readonly string? _scriptPath;

        private JetTestStore(
            string name,
            string? initScript = null,
            string? scriptPath = null,
            bool shared = true)
            : base(name + ".accdb", shared, CreateConnection(name))
        {
            if (initScript != null)
            {
                _initScript = initScript;
            }

            if (scriptPath != null)
            {
                _scriptPath = Path.Combine(Path.GetDirectoryName(typeof(JetTestStore).Assembly.Location)!, scriptPath);
            }
        }

        public async Task<JetTestStore> InitializeJetAsync(
            IServiceProvider? serviceProvider,
            Func<DbContext>? createContext,
            Func<DbContext, Task>? seed)
            => (JetTestStore)await InitializeAsync(serviceProvider, createContext, seed);

        public async Task<JetTestStore> InitializeJetAsync(
            IServiceProvider serviceProvider,
            Func<JetTestStore, DbContext> createContext,
            Func<DbContext, Task> seed)
            => await InitializeJetAsync(serviceProvider, () => createContext(this), seed);

        protected override async Task InitializeAsync(Func<DbContext> createContext, Func<DbContext, Task>? seed, Func<DbContext, Task>? clean)
        {
            if (CreateDatabase(clean))
            {
                if (_scriptPath != null)
                {
                    ExecuteScript(await File.ReadAllTextAsync(_scriptPath));
                }
                else
                {
                    using var context = createContext();
                    await context.Database.EnsureCreatedResilientlyAsync();

                    if (_initScript != null)
                    {
                        ExecuteScript(_initScript);
                    }

                    if (seed != null)
                    {
                        await seed(context);
                    }
                }
            }
        }

        public override DbContextOptionsBuilder AddProviderOptions(DbContextOptionsBuilder builder)
            => builder.UseJet(Connection, b => b.ApplyConfiguration().UseShortTextForSystemString()).EnableSensitiveDataLogging();

        private bool CreateDatabase(Func<DbContext, Task>? clean)
        {
            var connectionString = CreateConnectionString(Name);

            if (JetConnection.DatabaseExists(connectionString))
            {
                // Only reseed scripted databases during CI runs
                if (_scriptPath != null && !TestEnvironment.IsCI)
                {
                    //return false;
                }

                // Delete the database to ensure it's recreated with the correct file path
                DeleteDatabase();
            }

            JetConnection.CreateDatabase(connectionString);
            //WaitForExists((JetConnection)Connection);
            return true;
        }

        public override Task CleanAsync(DbContext context)
        {
            context.Database.EnsureClean();
            return Task.CompletedTask;
        }

        public void ExecuteScript(string script)
            => Execute(
                Connection, command =>
                {
                    foreach (var batch in
                             new Regex(@"\s*;\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromMilliseconds(1000.0))
                                 .Split(script).Where(b => !string.IsNullOrEmpty(b)))
                    {
                        command.CommandText = batch;
                        command.ExecuteNonQuery();
                    }

                    return 0;
                }, "");

        private static void WaitForExists(JetConnection connection)
            => new TestJetRetryingExecutionStrategy().Execute(connection, WaitForExistsImplementation);

        private static void WaitForExistsImplementation(JetConnection connection)
        {
            var retryCount = 0;
            while (true)
            {
                try
                {
                    if (connection.State != ConnectionState.Closed)
                    {
                        connection.Close();
                    }

                    JetConnection.ClearPool(connection);

                    connection.Open();
                    connection.Close();
                    return;
                }
                catch (Exception /*e*/)
                {
                    if (++retryCount >= 30
                        /*|| e.Number != 233 && e.Number != -2 && e.Number != 4060 && e.Number != 1832 && e.Number != 5120*/)
                    {
                        throw;
                    }

                    Thread.Sleep(100);
                }
            }
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
            => Execute(connection, command => (T)command.ExecuteScalar()!, sql, false, parameters);

        public Task<T> ExecuteScalarAsync<T>(string sql, params object[] parameters)
            => ExecuteScalarAsync<T>(Connection, sql, parameters);

        private static Task<T> ExecuteScalarAsync<T>(DbConnection connection, string sql, IReadOnlyList<object>? parameters = null)
            => ExecuteAsync(connection, async command => (T)(await command.ExecuteScalarAsync())!, sql, false, parameters);

        public int ExecuteNonQuery(string sql, params object[] parameters)
            => ExecuteNonQuery(Connection, sql, parameters);

        private static int ExecuteNonQuery(DbConnection connection, string sql, object[]? parameters = null)
            => Execute(connection, command => command.ExecuteNonQuery(), sql, false, parameters);

        public Task<int> ExecuteNonQueryAsync(string sql, params object[] parameters)
            => ExecuteNonQueryAsync(Connection, sql, parameters);

        private static Task<int> ExecuteNonQueryAsync(DbConnection connection, string sql, IReadOnlyList<object>? parameters = null)
            => ExecuteAsync(connection, command => command.ExecuteNonQueryAsync(), sql, false, parameters);

        public IEnumerable<T> Query<T>(string sql, params object[] parameters)
            => Query<T>(Connection, sql, parameters);

        private static IEnumerable<T> Query<T>(DbConnection connection, string sql, object[]? parameters = null)
            => Execute(
                connection, command =>
                {
                    using var dataReader = command.ExecuteReader();
                    var results = Enumerable.Empty<T>();
                    while (dataReader.Read())
                    {
                        results = results.Concat(new[] { dataReader.GetFieldValue<T>(0) });
                    }

                    return results;
                }, sql, false, parameters);

        public Task<IEnumerable<T>> QueryAsync<T>(string sql, params object[] parameters)
            => QueryAsync<T>(Connection, sql, parameters);

        private static Task<IEnumerable<T>> QueryAsync<T>(DbConnection connection, string sql, object[]? parameters = null)
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
            bool useTransaction = false, object[]? parameters = null)
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

        private static T ExecuteCommand<T>(
            DbConnection connection, Func<DbCommand, T> execute, string sql, bool useTransaction, object[]? parameters)
        {
            if (connection.State != ConnectionState.Closed)
            {
                connection.Close();
            }

            connection.Open();
            try
            {
                using var transaction = useTransaction ? connection.BeginTransaction() : null;
                T result;
                using (var command = CreateCommand(connection, sql, parameters))
                {
                    command.Transaction = transaction;
                    result = execute(command);
                }

                transaction?.Commit();

                return result;
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
            bool useTransaction = false, IReadOnlyList<object>? parameters = null)
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
            IReadOnlyList<object>? parameters)
        {
            if (connection.State != ConnectionState.Closed)
            {
                await connection.CloseAsync();
            }

            await connection.OpenAsync();
            try
            {
                using var transaction = useTransaction ? await connection.BeginTransactionAsync() : null;
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
            finally
            {
                if (connection.State != ConnectionState.Closed)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private static DbCommand CreateCommand(
            DbConnection connection, string commandText, IReadOnlyList<object>? parameters = null)
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

        private static JetConnection CreateConnection(string name)
        {
            var connectionString = CreateConnectionString(name);
            return new JetConnection(connectionString);
        }

        public static string CreateConnectionString(string name)
        {
            var defaultConnectionString = TestEnvironment.DefaultConnection;
            var dataAccessProviderFactory = JetFactory.Instance.GetDataAccessProviderFactory(JetConnection.GetDataAccessProviderType(defaultConnectionString));
            var connectionStringBuilder = dataAccessProviderFactory.CreateConnectionStringBuilder();

            connectionStringBuilder!.ConnectionString = defaultConnectionString;
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