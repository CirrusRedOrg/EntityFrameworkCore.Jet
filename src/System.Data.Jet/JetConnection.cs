using System.Data.Common;
using System.Data.Jet.JetStoreSchemaDefinition;
using System.Data.OleDb;
using System.Transactions;

namespace System.Data.Jet
{
    public class JetConnection : DbConnection, IDisposable, ICloneable
    {
        private ConnectionState _state;
        private string _ConnectionString;

        internal DbConnection InnerConnection { get; private set; }

        internal JetTransaction ActiveTransaction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="JetConnection"/> class.
        /// </summary>
        public JetConnection()
        {
            _state = ConnectionState.Closed;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JetConnection"/> class.
        /// </summary>
        /// <param name="connectionString">The connection string.</param>
        public JetConnection(string connectionString) : this()
        {
            this.ConnectionString = connectionString;
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
        protected override DbProviderFactory DbProviderFactory
        {
            get
            {
                return JetProviderFactory.Instance;
            }
        }

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
            if (_state == ConnectionState.Closed)
                return;
            if (ActiveTransaction != null)
                ActiveTransaction.Rollback();
            ActiveTransaction = null;
            _state = ConnectionState.Closed;
            if (InnerConnection != null)
            {
                InnerConnection.StateChange -= WrappedConnection_StateChange;
                InnerConnectionFactory.Instance.CloseConnection(_ConnectionString, InnerConnection);
            }
            InnerConnection = null;
            OnStateChange(new StateChangeEventArgs(ConnectionState.Open, ConnectionState.Closed));
        }

        /// <summary>
        /// Gets or sets the string used to open the connection.
        /// </summary>
        public override string ConnectionString
        {
            get
            {
                return _ConnectionString;
            }
            set
            {
                if (State != ConnectionState.Closed)
                    throw new InvalidOperationException(Messages.CannotChangePropertyValueInThisConnectionState(nameof(ConnectionString), State));
                _ConnectionString = value;
            }
        }

        /// <summary>
        /// Gets the time to wait while establishing a connection before terminating the attempt and generating an error.
        /// For Jet this time is unlimited
        /// </summary>
        public override int ConnectionTimeout
        {
            get
            {
                return 0;
            }
        }

        /// <summary>
        /// Creates and returns a <see cref="T:System.Data.Common.DbCommand" /> object associated with the current connection.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Data.Common.DbCommand" /> object.
        /// </returns>
        protected override DbCommand CreateDbCommand()
        {
            DbCommand command = JetProviderFactory.Instance.CreateCommand();
            command.Connection = this;
            return command;
        }

        /// <summary>
        /// This property is always empty in Jet. Use DataSource property instead.
        /// Gets the name of the current database after a connection is opened, or the database name specified 
        /// in the connection string before the connection is opened.
        /// </summary>
        public override string Database
        {
            get { return string.Empty; }
        }

        /// <summary>
        /// Gets the name of the file to open.
        /// </summary>
        public override string DataSource
        {
            get
            {
                OleDbConnectionStringBuilder connectionStringBuilder = new OleDbConnectionStringBuilder(_ConnectionString);
                return connectionStringBuilder.DataSource;
            }
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="T:System.ComponentModel.Component" /> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            _ConnectionString = string.Empty;

            if (disposing)
                Close();

            base.Dispose(disposing);
        }

        /// <summary>
        /// Enlists in the specified transaction.
        /// </summary>
        /// <param name="transaction">A reference to an existing <see cref="T:System.Transactions.Transaction" /> in which to enlist.</param>
        public override void EnlistTransaction(Transaction transaction)
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
            if (string.IsNullOrWhiteSpace(_ConnectionString))
                throw new InvalidOperationException(Messages.PropertyNotInitialized(nameof(ConnectionString)));
            if (State != ConnectionState.Closed)
                throw new InvalidOperationException(Messages.CannotCallMethodInThisConnectionState(nameof(Open), ConnectionState.Closed, State));

            _state = ConnectionState.Open;
            InnerConnection = InnerConnectionFactory.Instance.OpenConnection(_ConnectionString);
            InnerConnection.StateChange += WrappedConnection_StateChange;
            OnStateChange(new StateChangeEventArgs(ConnectionState.Closed, ConnectionState.Open));
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
            ConnectionState oldConnectionState = State;
            bool tableExists;

            if (oldConnectionState == ConnectionState.Closed)
                Open();

            try
            {
                string sqlFormat = "select count(*) from [{0}] where 1=2";
                CreateCommand(String.Format(sqlFormat, tableName)).ExecuteNonQuery();
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
            if (String.IsNullOrEmpty(commandText))
                // SqlCommand will complain if the command text is empty
                commandText = Environment.NewLine;

            var command = new JetCommand(commandText, this);
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
            JetConnection clone = new JetConnection();
            if (InnerConnection != null)
                clone.InnerConnection = InnerConnectionFactory.Instance.OpenConnection(_ConnectionString);
            return clone;
        }


        /// <summary>
        /// Performs an explicit conversion from <see cref="JetConnection"/> to <see cref="OleDbConnection"/>.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <returns>
        /// The result of the conversion.
        /// </returns>
        public static explicit operator OleDbConnection(JetConnection connection)
        {
            return (OleDbConnection)connection.InnerConnection;
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
            => AdoxWrapper.CreateEmptyDatabase(_ConnectionString);

        public static void CreateEmptyDatabase(string connectionString)
            => AdoxWrapper.CreateEmptyDatabase(connectionString);

        public static string GetConnectionString(string provider, string fileName)
            => $"Provider={provider};Data Source={fileName}";

        public static string GetConnectionString(string fileName)
            => $"Provider={JetConfiguration.OleDbDefaultProvider};Data Source={fileName}";

        public void DropDatabase()
            => DropDatabase(_ConnectionString);

        public static void DropDatabase(string connectionString)
        {
            var fileName = JetStoreDatabaseHandling.ExtractFileNameFromConnectionString(connectionString);
            
            if (string.IsNullOrWhiteSpace(fileName))
                throw new Exception("Cannot retrieve file name from connection string");

            JetStoreDatabaseHandling.DeleteFile(fileName);
        }

        public bool DatabaseExists()
            => DatabaseExists(_ConnectionString);

        public static bool DatabaseExists(string connectionString)
        {
            var fileName = JetStoreDatabaseHandling.ExtractFileNameFromConnectionString(connectionString)
                .Trim('"');
            
            if (string.IsNullOrWhiteSpace(fileName))
                throw new Exception("Cannot retrieve file name from connection string");
            
            return System.IO.File.Exists(fileName);
        }
    }
}
