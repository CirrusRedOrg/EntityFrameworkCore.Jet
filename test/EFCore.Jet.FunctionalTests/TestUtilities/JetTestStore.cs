using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.Jet;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace EntityFramework.Jet.FunctionalTests
{
    public class JetTestStore : RelationalTestStore
    {

        private const string NorthwindName = "NorthwindEF7";

        private static int _scratchCount;

        public static readonly string NorthwindConnectionString = CreateConnectionString(NorthwindName);

        private static string BaseDirectory => AppContext.BaseDirectory;

        private JetConnection _connection;
        //private JetTransaction _transaction;
        /// <summary>
        /// The database file name
        /// </summary>
        private readonly string _name;
        private string _connectionString;
        private bool _deleteDatabase;

        public override string ConnectionString => _connectionString;

        public JetTestStore(string name)
        {
            _name = name;
           
        }


        public string Name { get; }

        public static JetTestStore GetNorthwindStore()
            => JetTestStore.GetOrCreateShared(NorthwindName, () => { });

        public static JetTestStore GetOrCreateShared(string name, Action initializeDatabase)
        {
            return new JetTestStore(name).CreateShared(initializeDatabase);
        }

        public static JetTestStore Create(string name)
            => new JetTestStore(name).CreateTransient(true);

        public static JetTestStore CreateScratch(bool createDatabase)
        {
            string name;
            do
            {
                name = "scratch-" + Interlocked.Increment(ref _scratchCount);
            }
            while (File.Exists(name + ".accdb"));

            return new JetTestStore(name).CreateTransient(createDatabase);
        }

        public static Task<JetTestStore> CreateScratchAsync(bool createDatabase = true)
        {
            return Task.FromResult(CreateScratch(createDatabase));
        }

        private JetTestStore CreateShared(Action initializeDatabase)
        {
            _connectionString = CreateConnectionString(_name);
            _connection = new JetConnection(_connectionString);

            CreateShared(typeof(JetTestStore).Name + _name,
                () =>
                {
                    if (!Exists())
                    {
                        initializeDatabase?.Invoke();
                    }
                });

            return this;
        }

        private JetTestStore CreateTransient(bool createDatabase)
        {
            _connectionString = CreateConnectionString(_name);

            _connection = new JetConnection(_connectionString);

            if (createDatabase)
            {
                _connection.CreateEmptyDatabase();
                _connection.Open();
            }

            _deleteDatabase = true;

            return this;
        }

        public override DbConnection Connection => _connection;
        public override DbTransaction Transaction => null;

        public int ExecuteNonQuery(string sql, params object[] parameters)
        {
            using (var command = CreateCommand(sql, parameters))
            {
                return command.ExecuteNonQuery();
            }
        }

        public IEnumerable<T> Query<T>(string sql, params object[] parameters)
        {
            using (var command = CreateCommand(sql, parameters))
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
            }
        }

        public bool Exists()
        {
            return _connection.DatabaseExists();
        }

        private DbCommand CreateCommand(string commandText, object[] parameters)
        {
            var command = _connection.CreateCommand();

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

            if (_deleteDatabase)
            {
                _connection.DropDatabase(false);
            }
            Connection?.Dispose();
            base.Dispose();
        }

        public static string CreateConnectionString(string name)
        {
            return JetConnection.GetConnectionString(name + ".accdb");
        }



    }
}
