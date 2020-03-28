using System.Data.Common;
using System.Data.Jet.JetStoreSchemaDefinition;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace System.Data.Jet
{
    public class JetConnection : DbConnection, IDisposable, ICloneable
    {
        private ConnectionState _state;
        private string _connectionString;
        private bool _frozen;

        internal DbConnection InnerConnection { get; private set; }

        internal JetTransaction ActiveTransaction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="JetConnection"/> class.
        /// </summary>
        public JetConnection()
            : this(null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JetConnection"/> class.
        /// </summary>
        /// <param name="connectionString">The connection string.</param>
        public JetConnection(string connectionString)
            : this(connectionString, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JetConnection"/> class.
        /// </summary>
        /// <param name="dataAccessProviderFactory">The underlying provider factory to use by Jet. Supported are
        /// `OdbcFactory` and `OleDbFactory`.</param>
        public JetConnection(DbProviderFactory dataAccessProviderFactory)
            : this(null, dataAccessProviderFactory)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JetConnection"/> class.
        /// </summary>
        /// <param name="connectionString">The connection string.</param>
        /// <param name="dataAccessProviderFactory">The underlying provider factory to use by Jet. Supported are
        /// `OdbcFactory` and `OleDbFactory`.</param>
        public JetConnection(string connectionString, DbProviderFactory dataAccessProviderFactory)
        {
            ConnectionString = connectionString;

            if (dataAccessProviderFactory != null)
                DataAccessProviderFactory = dataAccessProviderFactory;

            _state = ConnectionState.Closed;
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is empty.
        /// It is similar to connection to master and can be used only to create and drop databases
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is empty; otherwise, <c>false</c>.
        /// </value>
        public bool IsEmpty { get; set; }

        /// <summary>
        /// Gets the <see cref="T:System.Data.Common.DbProviderFactory" /> for this <see cref="T:System.Data.Common.DbConnection" />.
        /// </summary>
        protected override DbProviderFactory DbProviderFactory => JetFactory;

        /// <summary>
        /// Gets or sets an `OdbcFactory` or `OleDbFactory` object, to use as the underlying data
        /// access API. Jet uses this provider factory internally for all data access operations.
        /// </summary>
        /// <exception cref="InvalidOperationException">This property can only be set as long as the connection is closed.</exception>
        public DbProviderFactory DataAccessProviderFactory
        {
            get => JetFactory?.InnerFactory;
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                if (JetFactory != null && JetFactory != value)
                    throw new InvalidOperationException($"The {DataAccessProviderFactory} property can only be set once.");

                JetFactory = new JetFactory(this, value);
            }
        }

        /// <summary>
        /// Gets a `JetProviderFactory` object, that can be used to create Jet specific objects (e.g. `JetCommand`).
        /// </summary>
        public JetFactory JetFactory { get; private set; }

        /// <summary>
        /// Starts a database transaction.
        /// </summary>
        /// <param name="isolationLevel">Specifies the isolation level for the transaction.</param>
        /// <returns>
        /// An object representing the new transaction.
        /// </returns>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            if (State != ConnectionState.Open)
                throw new InvalidOperationException(Messages.CannotCallMethodInThisConnectionState("BeginDbTransaction", State));

            if (ActiveTransaction != null)
                throw new InvalidOperationException(Messages.UnsupportedParallelTransactions());

            switch (isolationLevel)
            {
                case IsolationLevel.Serializable:
                    ActiveTransaction = CreateTransaction(IsolationLevel.ReadCommitted);
                    break;
                default:
                    ActiveTransaction = CreateTransaction(isolationLevel);
                    break;
            }

            return ActiveTransaction;
        }

        private JetTransaction CreateTransaction(IsolationLevel isolationLevel)
            => new JetTransaction(this, isolationLevel);

        /// <summary>
        /// Changes the current database for an open connection.
        /// </summary>
        /// <param name="databaseName">Specifies the name of the database for the connection to use.</param>
        public override void ChangeDatabase(string databaseName)
        {
            if (State != ConnectionState.Open)
                throw new InvalidOperationException(Messages.CannotCallMethodInThisConnectionState(nameof(ConnectionString), ConnectionState.Open, State));

            throw new InvalidOperationException(Messages.MethodUnsupportedByJet("ChangeDatabase"));
        }

        /// <summary>
        /// Closes the connection to the database. This is the preferred method of closing any open connection.
        /// </summary>
        public override void Close()
        {
            if (ActiveTransaction != null)
            {
                ActiveTransaction.Rollback();
                ActiveTransaction = null;
            }

            if (InnerConnection != null)
            {
                InnerConnection.StateChange -= WrappedConnection_StateChange;
                InnerConnectionFactory.Instance.CloseConnection(_connectionString, InnerConnection);
                InnerConnection = null;
            }

            if (_state == ConnectionState.Closed)
                return;

            _state = ConnectionState.Closed;
            OnStateChange(new StateChangeEventArgs(ConnectionState.Open, ConnectionState.Closed));
        }

        /// <summary>
        /// Gets or sets the string used to open the connection.
        /// </summary>
        public override string ConnectionString
        {
            get => _connectionString;
            set
            {
                if (State != ConnectionState.Closed)
                    throw new InvalidOperationException(Messages.CannotChangePropertyValueInThisConnectionState(nameof(ConnectionString), State));
                
                if (_frozen)
                    throw new InvalidOperationException($"Cannot modify \"{nameof(ConnectionString)}\" property after the connection has been frozen.");
                
                _connectionString = value;
            }
        }

        /// <summary>
        /// Gets the time to wait while establishing a connection before terminating the attempt and generating an error.
        /// For Jet this time is unlimited
        /// </summary>
        public override int ConnectionTimeout
            => 0;

        /// <summary>
        /// Creates and returns a <see cref="T:System.Data.Common.DbCommand" /> object associated with the current connection.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Data.Common.DbCommand" /> object.
        /// </returns>
        protected override DbCommand CreateDbCommand()
        {
            var command = JetFactory.CreateCommand();
            command.Connection = this;
            return command;
        }

        /// <summary>
        /// This property is always empty in Jet. Use DataSource property instead.
        /// Gets the name of the current database after a connection is opened, or the database name specified 
        /// in the connection string before the connection is opened.
        /// </summary>
        public override string Database
            => string.Empty;

        /// <summary>
        /// Gets the name of the file to open.
        /// </summary>
        public override string DataSource
            => GetDatabaseFilePath(_connectionString);

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="T:System.ComponentModel.Component" /> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            _connectionString = string.Empty;

            if (disposing)
                Close();

            base.Dispose(disposing);
        }

        /// <summary>
        /// Enlists in the specified transaction.
        /// </summary>
        /// <param name="transaction">A reference to an existing <see cref="T:System.Transactions.Transaction" /> in which to enlist.</param>
        public override void EnlistTransaction(System.Transactions.Transaction transaction)
        {
            if (InnerConnection == null)
                throw new InvalidOperationException(Messages.PropertyNotInitialized("Connection"));
            InnerConnection.EnlistTransaction(transaction);
        }

        /// <summary>
        /// Returns schema information for the data source of this <see cref="T:System.Data.Common.DbConnection" /> using the specified string for the schema name.
        /// </summary>
        /// <param name="collectionName">Specifies the name of the schema to return.</param>
        /// <returns>
        /// A <see cref="T:System.Data.DataTable" /> that contains schema information.
        /// </returns>
        /// <PermissionSet>
        ///   <IPermission class="System.Security.Permissions.FileIOPermission, mscorlib, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" version="1" PathDiscovery="*AllFiles*" />
        /// </PermissionSet>
        public override DataTable GetSchema(string collectionName)
        {
            if (State != ConnectionState.Open)
                throw new InvalidOperationException(Messages.CannotCallMethodInThisConnectionState("GetSchema", State));
            return InnerConnection.GetSchema(collectionName);
        }

        /// <summary>
        /// Returns schema information for the data source of this <see cref="T:System.Data.Common.DbConnection" />.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Data.DataTable" /> that contains schema information.
        /// </returns>
        /// <PermissionSet>
        ///   <IPermission class="System.Security.Permissions.FileIOPermission, mscorlib, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" version="1" PathDiscovery="*AllFiles*" />
        /// </PermissionSet>
        public override DataTable GetSchema()
        {
            if (State != ConnectionState.Open)
                throw new InvalidOperationException(Messages.CannotCallMethodInThisConnectionState("GetSchema", State));
            return InnerConnection.GetSchema();
        }

        /// <summary>
        /// Returns schema information for the data source of this <see cref="T:System.Data.Common.DbConnection" /> using the specified string for the schema name and the specified string array for the restriction values.
        /// </summary>
        /// <param name="collectionName">Specifies the name of the schema to return.</param>
        /// <param name="restrictionValues">Specifies a set of restriction values for the requested schema.</param>
        /// <returns>
        /// A <see cref="T:System.Data.DataTable" /> that contains schema information.
        /// </returns>
        /// <PermissionSet>
        ///   <IPermission class="System.Security.Permissions.FileIOPermission, mscorlib, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" version="1" PathDiscovery="*AllFiles*" />
        /// </PermissionSet>
        public override DataTable GetSchema(string collectionName, string[] restrictionValues)
        {
            if (State != ConnectionState.Open)
                throw new InvalidOperationException(Messages.CannotCallMethodInThisConnectionState("GetSchema", State));
            return InnerConnection.GetSchema(collectionName, restrictionValues);
        }

        /// <summary>
        /// Opens a database connection with the settings specified by the <see cref="P:System.Data.Common.DbConnection.ConnectionString" />.
        /// </summary>
        public override void Open()
        {
            if (IsEmpty)
                return;
            if (string.IsNullOrWhiteSpace(_connectionString))
                throw new InvalidOperationException(Messages.PropertyNotInitialized(nameof(ConnectionString)));
            if (State != ConnectionState.Closed)
                throw new InvalidOperationException(Messages.CannotCallMethodInThisConnectionState(nameof(Open), ConnectionState.Closed, State));

            if (JetFactory == null)
            {
                var dataAccessProviderType = GetDataAccessProviderType(ConnectionString);
                DataAccessProviderFactory = JetFactory.Instance.GetDataAccessProviderFactory(dataAccessProviderType);
            }

            try
            {
                InnerConnection = InnerConnectionFactory.Instance.OpenConnection(
                    ExpandDatabaseFilePath(_connectionString),
                    JetFactory.InnerFactory);
                InnerConnection.StateChange += WrappedConnection_StateChange;

                _state = ConnectionState.Open;
                OnStateChange(new StateChangeEventArgs(ConnectionState.Closed, ConnectionState.Open));
            }
            catch
            {
                Close();
                throw;
            }
        }

        /// <summary>
        /// Gets a string that represents the version of the server to which the object is connected.
        /// </summary>
        public override string ServerVersion
        {
            get
            {
                if (State != ConnectionState.Open)
                    throw new InvalidOperationException(Messages.CannotReadPropertyValueInThisConnectionState(nameof(ServerVersion), State));
                return InnerConnection.ServerVersion;
            }
        }

        /// <summary>
        /// Gets a string that describes the state of the connection.
        /// </summary>
        public override ConnectionState State
        {
            get { return _state; }
        }

        void WrappedConnection_StateChange(object sender, StateChangeEventArgs e)
        {
            OnStateChange(e);
        }

        public bool TableExists(string tableName)
        {
            var oldConnectionState = State;
            bool tableExists;

            if (oldConnectionState == ConnectionState.Closed)
                Open();

            try
            {
                var sqlFormat = "select count(*) from [{0}] where 1=2";
                CreateCommand(string.Format(sqlFormat, tableName))
                    .ExecuteNonQuery();
                tableExists = true;
            }
            catch
            {
                tableExists = false;
            }

            if (oldConnectionState == ConnectionState.Closed)
                Close();

            return tableExists;
        }

        public DbCommand CreateCommand(string commandText, int? commandTimeout = null)
        {
            if (JetFactory == null)
                throw new InvalidOperationException(Messages.PropertyNotInitialized(nameof(DataAccessProviderFactory)));

            if (string.IsNullOrEmpty(commandText))
                // SqlCommand will complain if the command text is empty
                commandText = Environment.NewLine;

            var command = (JetCommand) JetFactory.CreateCommand();
            command.CommandText = commandText;

            if (commandTimeout.HasValue)
                command.CommandTimeout = commandTimeout.Value;

            return command;
        }

        /// <summary>
        /// Creates a new object that is a copy of the current instance.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        object ICloneable.Clone()
        {
            var clone = new JetConnection();
            if (InnerConnection != null)
                clone.InnerConnection = InnerConnectionFactory.Instance.OpenConnection(_connectionString, JetFactory.InnerFactory);
            return clone;
        }

        public void Freeze()
        {
            if (!string.IsNullOrWhiteSpace(ConnectionString))
                _frozen = true;
        }

        /// <summary>
        /// Clears the pool.
        /// </summary>
        /// <param name="connection">The connection.</param>
        public static void ClearPool(JetConnection connection)
        {
            // Actually Jet does not support pools
        }

        /// <summary>
        /// Clears all pools.
        /// </summary>
        public static void ClearAllPools()
            => InnerConnectionFactory.Instance.ClearAllPools();

        public void CreateEmptyDatabase()
            => CreateEmptyDatabase(DataSource, DataAccessProviderFactory);

        public static string CreateEmptyDatabase(string fileName, DbProviderFactory dataAccessProviderFactory)
            => AdoxWrapper.CreateEmptyDatabase(fileName, dataAccessProviderFactory);

        public static string GetConnectionString(string fileName, DbProviderFactory dataAccessProviderFactory)
            => GetConnectionString(fileName, GetDataAccessProviderType(dataAccessProviderFactory));

        public static string GetConnectionString(string fileName, DataAccessProviderType dataAccessProviderType)
            => GetConnectionString(
                dataAccessProviderType == DataAccessProviderType.OleDb
                    ? JetConfiguration.OleDbDefaultProvider
                    : JetConfiguration.OdbcDefaultProvider,
                fileName,
                dataAccessProviderType);

        public static string GetConnectionString(string provider, string fileName, DbProviderFactory dataAccessProviderFactory)
            => GetConnectionString(provider, fileName, GetDataAccessProviderType(dataAccessProviderFactory));
        
        public static string GetConnectionString(string provider, string fileName, DataAccessProviderType dataAccessProviderType)
            => dataAccessProviderType == DataAccessProviderType.OleDb
                ? $"Provider={provider};Data Source={fileName}"
                : $"Driver={{{provider}}};DBQ={fileName}";

        private string GetDatabaseFilePath(string connectionString)
        {
            var connectionStringBuilder = JetFactory.InnerFactory.CreateConnectionStringBuilder();
            connectionStringBuilder.ConnectionString = connectionString;
            return connectionStringBuilder.GetDataSource();
        }

        private string ExpandDatabaseFilePath(string connectionString)
        {
            var connectionStringBuilder = JetFactory.InnerFactory.CreateConnectionStringBuilder();
            connectionStringBuilder.ConnectionString = connectionString;
            connectionStringBuilder.SetDataSource(JetStoreDatabaseHandling.ExpandFileName(connectionStringBuilder.GetDataSource()));
            return connectionStringBuilder.ConnectionString;
        }

        public void DropDatabase()
            => DropDatabase(_connectionString);

        public static void DropDatabase(string fileNameOrConnectionString)
        {
            var fileName = JetStoreDatabaseHandling.ExtractFileNameFromConnectionString(fileNameOrConnectionString);

            if (string.IsNullOrWhiteSpace(fileName))
                throw new InvalidOperationException("The file name or connection string is invalid.");

            JetStoreDatabaseHandling.DeleteFile(fileName);
        }

        public bool DatabaseExists()
            => DatabaseExists(_connectionString);

        public static bool DatabaseExists(string fileNameOrConnectionString)
        {
            var fileName = JetStoreDatabaseHandling.ExtractFileNameFromConnectionString(fileNameOrConnectionString)
                .Trim('"');

            if (string.IsNullOrWhiteSpace(fileName))
                throw new InvalidOperationException("The file name or connection string is invalid.");

            return File.Exists(fileName);
        }

        public static DataAccessProviderType GetDataAccessProviderType(string connectionString)
        {
            var isOleDb = Regex.IsMatch(connectionString, @"Provider\s*=\s*\w+", RegexOptions.IgnoreCase);
            var isOdbc = Regex.IsMatch(connectionString, @"Driver\s*=\s*\{?\w+\}?", RegexOptions.IgnoreCase);

            if (isOdbc && isOleDb)
                throw new InvalidOperationException("The connection string appears to be for ODBC and OLE DB. Only one distinct style is supported at a time.");

            if (!isOdbc && !isOleDb)
                throw new ArgumentException("The connection string appears to be neither ODBC nor OLE DB compliant.", nameof(connectionString));

            return isOleDb
                ? DataAccessProviderType.OleDb
                : DataAccessProviderType.Odbc;
        }

        public static DataAccessProviderType GetDataAccessProviderType(DbProviderFactory providerFactory)
        {
            var isOleDb = providerFactory
                .GetType()
                .GetTypesInHierarchy()
                .FirstOrDefault(
                    t => string.Equals(
                        t.FullName,
                        "System.Data.OleDb.OleDbFactory",
                        StringComparison.OrdinalIgnoreCase)) != null;

            var isOdbc = providerFactory
                .GetType()
                .GetTypesInHierarchy()
                .FirstOrDefault(
                    t => string.Equals(
                        t.FullName,
                        "System.Data.Odbc.OdbcFactory",
                        StringComparison.OrdinalIgnoreCase)) != null;

            if (isOdbc && isOleDb)
                throw new InvalidOperationException();

            if (!isOdbc && !isOleDb)
                throw new ArgumentException($"The parameter is neither of type OdbcFactory nor OleDbFactory.", nameof(providerFactory));

            return isOleDb
                ? DataAccessProviderType.OleDb
                : DataAccessProviderType.Odbc;
        }
    }
}