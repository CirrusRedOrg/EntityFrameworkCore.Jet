using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Jet;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EntityFramework.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFramework.Jet.FunctionalTests
{
    public class JetTestStore : RelationalTestStore
    {
        public const int CommandTimeout = 600;

        private const string NorthwindName = "NorthwindEF7";

        private static int _scratchCount;

        public static readonly string NorthwindConnectionString = CreateConnectionString(NorthwindName);

        private static string BaseDirectory => AppContext.BaseDirectory;

        //private JetTransaction _transaction;
        /// <summary>
        /// The database file name
        /// </summary>
        private readonly string _name;
        private readonly string _scriptPath;
        private readonly bool _deleteDatabase;

        [SuppressMessage("ReSharper", "VirtualMemberCallInConstructor")]
        private JetTestStore(string name, bool deleteDatabase = false, string scriptPath = null, bool shared = true) : base(name, shared)
        {
            _name = name;
            _deleteDatabase = deleteDatabase;
            _scriptPath = scriptPath;
            ConnectionString = CreateConnectionString(name);
            Connection = new JetConnection(ConnectionString);
        }

        public static JetTestStore GetOrCreate(string name)
            => new JetTestStore(name);

        public static JetTestStore GetOrCreateInitialized(string name)
            => new JetTestStore(name).InitializeJet(null, (Func<DbContext>)null, null);

        public static JetTestStore GetOrCreate(string name, string scriptPath)
            => new JetTestStore(name, scriptPath: scriptPath);

        public static JetTestStore GetNorthwindStore()
            => (JetTestStore)JetNorthwindTestStoreFactory.Instance
                .GetOrCreate(JetNorthwindTestStoreFactory.Name).Initialize(null, (Func<DbContext>)null, null);


        public static JetTestStore Create(string name, bool useFileName = false)
            => new JetTestStore(name, shared: false);

        public static JetTestStore CreateInitialized(string name, bool useFileName = false, bool? multipleActiveResultSets = null)
            => new JetTestStore(name, shared: false)
                .InitializeJet(null, (Func<DbContext>)null, null);

        public static JetTestStore CreateScratch(bool createDatabase)
        {
            string name;
            do
            {
                name = "scratch-" + Interlocked.Increment(ref _scratchCount);
            }
            while (File.Exists(name + ".accdb"));
            JetConnection.CreateEmptyDatabase(JetConnection.GetConnectionString(name + ".accdb"));
            return new JetTestStore(name, deleteDatabase: true);
        }

        public static Task<JetTestStore> CreateScratchAsync(bool createDatabase = true)
        {
            return Task.FromResult(CreateScratch(createDatabase));
        }


        public JetTestStore InitializeJet(
            IServiceProvider serviceProvider, Func<DbContext> createContext, Action<DbContext> seed)
            => (JetTestStore)Initialize(serviceProvider, createContext, seed);

        public JetTestStore InitializeJet(
            IServiceProvider serviceProvider, Func<JetTestStore, DbContext> createContext, Action<DbContext> seed)
            => InitializeJet(serviceProvider, () => createContext(this), seed);

        protected override void Initialize(Func<DbContext> createContext, Action<DbContext> seed)
        {
            if (CreateDatabase())
            {
                if (_scriptPath != null)
                {
                    ExecuteScript(_scriptPath);
                }
                else
                {
                    using (var context = createContext())
                    {
                        context.Database.EnsureCreated();
                        seed(context);
                    }
                }
            }
        }

        public void ExecuteScript(string scriptPath)
        {
            var script = File.ReadAllText(scriptPath);
            Execute(
                Connection, command =>
                {
                    foreach (var batch in
                        new Regex("^GO", RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromMilliseconds(1000.0))
                            .Split(script).Where(b => !string.IsNullOrEmpty(b)))
                    {
                        command.CommandText = batch;
                        command.ExecuteNonQuery();
                    }

                    return 0;
                }, "");
        }

        private static T Execute<T>(
            DbConnection connection, Func<DbCommand, T> execute, string sql,
            bool useTransaction = false, object[] parameters = null)
            =>
            ExecuteCommand(connection, execute, sql, useTransaction, parameters);


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
                using (var transaction = useTransaction ? connection.BeginTransaction() : null)
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

        private bool CreateDatabase()
        {
            if (File.Exists(_name + ".accdb"))
            {
                if (_scriptPath != null)
                {
                    return false;
                }

                using (var context = new DbContext(AddProviderOptions(new DbContextOptionsBuilder()).Options))
                {
                    Clean(context);
                    return true;
                }

            }

            JetConnection.CreateEmptyDatabase(JetConnection.GetConnectionString(_name + ".accdb"));
            return true;
        }

        private void DeleteDatabase()
        {
            JetConnection.ClearAllPools();

            if (File.Exists(_name + ".accdb"))
                File.Delete(_name + ".accdb");

        }

        public override DbContextOptionsBuilder AddProviderOptions(DbContextOptionsBuilder builder)
            => builder.UseJet(Connection, b => b.ApplyConfiguration().CommandTimeout(CommandTimeout));


        public override void Clean(DbContext context)
            => context.Database.EnsureClean();


        public DbTransaction Transaction => null;
        public ConnectionState State => Connection.State;

        public int ExecuteNonQuery(string sql, params object[] parameters)
        {
            var connectionState = Connection.State;
            if (connectionState != ConnectionState.Open)
                Connection.Open();
            int result;
            using (var command = CreateCommand(Connection, sql, parameters))
            {
                result = command.ExecuteNonQuery();
            }
            if (connectionState != ConnectionState.Open)
                Connection.Close();
            return result;
        }

        public IEnumerable<T> Query<T>(string sql, params object[] parameters)
        {
            var connectionState = Connection.State;
            if (connectionState != ConnectionState.Open)
                Connection.Open();

            var results = Enumerable.Empty<T>();
            using (var command = CreateCommand(Connection, sql, parameters))
            {
                using (var dataReader = command.ExecuteReader())
                {
                    while (dataReader.Read())
                    {
                        results = results.Concat(new[] { dataReader.GetFieldValue<T>(0) });
                    }
                }
            }
            if (connectionState != ConnectionState.Open)
                Connection.Close();
            return results;
        }

        public bool Exists()
        {
            return ((JetConnection)Connection).DatabaseExists();
        }

        private static DbCommand CreateCommand(DbConnection connection, string commandText, object[] parameters)
        {
            var command = connection.CreateCommand();

            command.CommandText = commandText;

            for (var i = 0; i < parameters.Length; i++)
            {
                command.Parameters.AddWithValue("p" + i, parameters[i]);
            }

            return command;
        }

        public override void Dispose()
        {
            Transaction?.Dispose();
            Connection?.Dispose();
            if (_deleteDatabase)
                JetConnection.DropDatabase(ConnectionString);

            base.Dispose();
        }

        public async Task OpenAsync() => await Connection?.OpenAsync();

        public void Open() => Connection?.Open();

        public static string CreateConnectionString(string name)
        {
            if (!name.Contains("."))
                return JetConnection.GetConnectionString(name + ".accdb");
            else
                return JetConnection.GetConnectionString(name);
        }


        public void Close() => Connection?.Close();

        

    }
}
