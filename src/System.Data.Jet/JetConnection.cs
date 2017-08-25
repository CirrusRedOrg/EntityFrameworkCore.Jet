using System;
using System.ComponentModel;
using System.Data.Common;
using System.Data.Jet.JetStoreSchemaDefinition;
using System.Data.OleDb;
using System.Reflection;
using System.Transactions;

namespace System.Data.Jet
{
    public class JetConnection : DbConnection, IDisposable, ICloneable
    {

        internal DbConnection WrappedConnection { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="JetConnection"/> class.
        /// </summary>
        public JetConnection()
        {
            WrappedConnection = new OleDbConnection();
            WrappedConnection.StateChange += WrappedConnection_StateChange;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JetConnection"/> class.
        /// </summary>
        /// <param name="connection">The underling OleDb connection.</param>
        public JetConnection(OleDbConnection connection)
        {
            WrappedConnection = connection;
            WrappedConnection.StateChange += WrappedConnection_StateChange;
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
            switch (isolationLevel)
            {
                case IsolationLevel.Serializable:
                    return new JetTransaction(WrappedConnection.BeginTransaction(IsolationLevel.ReadCommitted), this);
                case IsolationLevel.Chaos:
                case IsolationLevel.ReadCommitted:
                case IsolationLevel.ReadUncommitted:
                case IsolationLevel.RepeatableRead:
                case IsolationLevel.Snapshot:
                case IsolationLevel.Unspecified:
                default:
                    return new JetTransaction(WrappedConnection.BeginTransaction(isolationLevel), this);
            }
        }

        /// <summary>
        /// Changes the current database for an open connection.
        /// </summary>
        /// <param name="databaseName">Specifies the name of the database for the connection to use.</param>
        public override void ChangeDatabase(string databaseName)
        {
            this.WrappedConnection.ChangeDatabase(databaseName);
        }

        /// <summary>
        /// Closes the connection to the database. This is the preferred method of closing any open connection.
        /// </summary>
        public override void Close()
        {
            this.WrappedConnection.Close();
        }

        /// <summary>
        /// Gets or sets the string used to open the connection.
        /// </summary>
        public override string ConnectionString
        {
            get
            {
                return this.WrappedConnection.ConnectionString;
            }
            set
            {
                this.WrappedConnection.ConnectionString = value;
            }
        }

        /// <summary>
        /// Gets the time to wait while establishing a connection before terminating the attempt and generating an error.
        /// </summary>
        public override int ConnectionTimeout
        {
            get
            {
                return this.WrappedConnection.ConnectionTimeout;
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
        /// Gets the name of the current database after a connection is opened, or the database name specified in the connection string before the connection is opened.
        /// </summary>
        public override string Database
        {
            get { return this.WrappedConnection.Database; }
        }

        /// <summary>
        /// Gets the name of the database server to which to connect.
        /// </summary>
        public override string DataSource
        {
            get { return this.WrappedConnection.DataSource; }
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="T:System.ComponentModel.Component" /> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                this.WrappedConnection.Dispose();
            base.Dispose(disposing);
            OnDisposed(EventArgs.Empty);
        }

        /// <summary>
        /// Enlists in the specified transaction.
        /// </summary>
        /// <param name="transaction">A reference to an existing <see cref="T:System.Transactions.Transaction" /> in which to enlist.</param>
        public override void EnlistTransaction(Transaction transaction)
        {
            this.WrappedConnection.EnlistTransaction(transaction);
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
            return this.WrappedConnection.GetSchema(collectionName);
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
            return this.WrappedConnection.GetSchema();
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
            return this.WrappedConnection.GetSchema(collectionName, restrictionValues);
        }

        /// <summary>
        /// Opens a database connection with the settings specified by the <see cref="P:System.Data.Common.DbConnection.ConnectionString" />.
        /// </summary>
        public override void Open()
        {
            if (IsEmpty)
                return;
            this.WrappedConnection.Open();
        }

        /// <summary>
        /// Gets a string that represents the version of the server to which the object is connected.
        /// </summary>
        public override string ServerVersion
        {
            get { return this.WrappedConnection.ServerVersion; }
        }

        /// <summary>
        /// Gets or sets the <see cref="T:System.ComponentModel.ISite" /> of the <see cref="T:System.ComponentModel.Component" />.
        /// </summary>
        public override ISite Site
        {
            get
            {
                return this.WrappedConnection.Site;
            }
            set
            {
                this.WrappedConnection.Site = value;
            }
        }

        /// <summary>
        /// Gets a string that describes the state of the connection.
        /// </summary>
        public override ConnectionState State
        {
            get { return this.WrappedConnection.State; }
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
        /// Occurs when the component is disposed by a call to the <see cref="M:System.ComponentModel.Component.Dispose" /> method.
        /// </summary>
        public new event EventHandler Disposed;

        /// <summary>
        /// Raises the <see cref="E:Disposed" /> event.
        /// </summary>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected virtual void OnDisposed(EventArgs e)
        {
            if (Disposed != null)
                Disposed(this, e);
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
            clone.WrappedConnection = (DbConnection)((ICloneable)this.WrappedConnection).Clone();
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
            return (OleDbConnection)connection.WrappedConnection;
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
        {
            // Actually Jet does not support pools
        }

        public void CreateEmptyDatabase()
        {
            CreateEmptyDatabase(WrappedConnection.ConnectionString);
        }

        public static void CreateEmptyDatabase(string connectionString)
        {
            Type adoxCatalogType;
            object catalog;

            try
            {
                adoxCatalogType = Type.GetTypeFromProgID("ADOX.Catalog", true);
            }
            catch (Exception e)
            {
                throw new Exception("Cannot create database. Cannot retrieve ADOX.Catalog type. Check ADOX installation.", e);
            }

            try
            {
                catalog = System.Activator.CreateInstance(adoxCatalogType);
            }
            catch (Exception e)
            {
                throw new Exception("Cannot create database. Cannot create an instance of ADOX.Catalog type.", e);
            }

            try
            {
                adoxCatalogType.InvokeMember("Create", BindingFlags.InvokeMethod, null, catalog, new object[] { connectionString });
            }
            catch (Exception e)
            {
                throw new Exception("Cannot create database using the specified connection string.", e);
            }

            try
            {
                object activeConnection = adoxCatalogType.InvokeMember("ActiveConnection", BindingFlags.GetProperty, null, catalog, null);
                activeConnection.GetType().InvokeMember("Close", BindingFlags.InvokeMethod, null, activeConnection, null);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Cannot close active connection after create statement.\r\nThe exception is: {0}", e.Message);
            }

        }

        public static string GetConnectionString(string fileName)
        {
            return $"Provider={JetConfiguration.OleDbDefaultProvider};Data Source={fileName}";
        }

        public void DropDatabase(bool throwOnError = true)
        {
            DropDatabase(WrappedConnection.ConnectionString, throwOnError);
        }

        public static void DropDatabase(string connectionString, bool throwOnError = true)
        {
            string fileName = JetStoreDatabaseHandling.ExtractFileNameFromConnectionString(connectionString);
            if (string.IsNullOrWhiteSpace(fileName))
                throw new Exception("Cannot retrieve file name from connection string");

            try
            {
                System.IO.File.Delete(fileName.Trim());
            }
            catch
            {
                if (throwOnError)
                    throw;
            }
        }

        public bool DatabaseExists()
        {
            return DatabaseExists(WrappedConnection.ConnectionString);
        }

        public static bool DatabaseExists(string connectionString)
        {
            string fileName = JetStoreDatabaseHandling.ExtractFileNameFromConnectionString(connectionString);
            if (string.IsNullOrWhiteSpace(fileName))
                throw new Exception("Cannot retrieve file name from connection string");
            return System.IO.File.Exists(fileName);
        }


    }
}
