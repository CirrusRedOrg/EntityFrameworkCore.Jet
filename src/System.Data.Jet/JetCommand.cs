using System;
using System.Data.Common;
using System.Data.Jet.JetStoreSchemaDefinition;
using System.Data.OleDb;

namespace System.Data.Jet
{
    class JetCommand : DbCommand, ICloneable
    {
        private DbCommand _WrappedCommand;
        private JetConnection _Connection;
        private JetTransaction _Transaction;
        private bool _DesignTimeVisible;

        /// <summary>
        /// Initializes a new instance of the <see cref="JetCommand"/> class.
        /// </summary>
        public JetCommand()
        {
            Initialize(null, null, null);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JetCommand"/> class.
        /// </summary>
        /// <param name="commandText">The command text.</param>
        public JetCommand(string commandText)
        {
            this.Initialize(commandText, null, null);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JetCommand"/> class.
        /// </summary>
        /// <param name="commandText">The command text.</param>
        /// <param name="connection">The connection.</param>
        public JetCommand(string commandText, JetConnection connection)
        {
            this.Initialize(commandText, connection, null);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JetCommand"/> class.
        /// </summary>
        /// <param name="commandText">The command text.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="transaction">The transaction.</param>
        public JetCommand(string commandText, JetConnection connection, DbTransaction transaction)
        {
            Initialize(commandText, connection, transaction);
        }

        private void Initialize(string commandText, JetConnection connection, DbTransaction transaction)
        {
            _Connection = null;
            _Transaction = null;
            _DesignTimeVisible = true;
            _WrappedCommand = new OleDbCommand();
            this.CommandText = commandText;
            this.Connection = connection;
            this.Transaction = transaction;
        }

        /// <summary>
        /// Attempts to Cancels the command execution
        /// </summary>
        public override void Cancel()
        {
            this._WrappedCommand.Cancel();
        }

        /// <summary>
        /// Gets or sets the command text.
        /// </summary>
        /// <value>
        /// The command text.
        /// </value>
        public override string CommandText
        {
            get
            {
                return this._WrappedCommand.CommandText;
            }
            set
            {
                this._WrappedCommand.CommandText = value;
            }
        }

        /// <summary>
        /// Gets or sets the command timeout.
        /// </summary>
        /// <value>
        /// The command timeout.
        /// </value>
        public override int CommandTimeout
        {
            get
            {
                return this._WrappedCommand.CommandTimeout;
            }
            set
            {
                this._WrappedCommand.CommandTimeout = value;
            }
        }

        /// <summary>
        /// Gets or sets the type of the command.
        /// </summary>
        /// <value>
        /// The type of the command.
        /// </value>
        public override CommandType CommandType
        {
            get
            {
                return this._WrappedCommand.CommandType;
            }
            set
            {
                this._WrappedCommand.CommandType = value;
            }
        }

        /// <summary>
        /// Creates the database parameter.
        /// </summary>
        /// <returns></returns>
        protected override DbParameter CreateDbParameter()
        {
            return this._WrappedCommand.CreateParameter();
        }

        /// <summary>
        /// Gets or sets the database connection.
        /// </summary>
        /// <value>
        /// The database connection.
        /// </value>
        protected override DbConnection DbConnection
        {
            get
            {
                return this._Connection;
            }
            set
            {
                if (value == null)
                {
                    this._Connection = null;
                    this._WrappedCommand.Connection = null;
                }
                else
                {
                    if (!typeof(JetConnection).IsAssignableFrom(value.GetType()))
                        throw new InvalidOperationException("The JetCommand connection should be a JetConnection");

                    this._Connection = (JetConnection)value;
                    this._WrappedCommand.Connection = this._Connection.WrappedConnection;
                }
            }
        }

        /// <summary>
        /// Gets the database parameter collection.
        /// </summary>
        /// <value>
        /// The database parameter collection.
        /// </value>
        protected override DbParameterCollection DbParameterCollection
        {
            get { return this._WrappedCommand.Parameters; }
        }

        /// <summary>
        /// Gets or sets the database transaction.
        /// </summary>
        /// <value>
        /// The database transaction.
        /// </value>
        protected override DbTransaction DbTransaction
        {
            get
            {
                return this._Transaction;
            }
            set
            {
                this._Transaction = (JetTransaction)value;
                this._WrappedCommand.Transaction = _Transaction == null ? null : _Transaction.WrappedTransaction;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether is design time visible.
        /// </summary>
        /// <value>
        ///   <c>true</c> if design time visible; otherwise, <c>false</c>.
        /// </value>
        public override bool DesignTimeVisible
        {
            get
            {
                return this._DesignTimeVisible;
            }
            set
            {
                this._DesignTimeVisible = value;
            }
        }
        /// <summary>
        /// Executes the database data reader.
        /// </summary>
        /// <param name="behavior">The behavior.</param>
        /// <returns></returns>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            ShowCommandText("ExecuteDbDataReader");

            DbDataReader dataReader;

            if (JetStoreSchemaDefinitionRetrieve.TryGetDataReaderFromShowCommand(_WrappedCommand, out dataReader))
            {
                // Retrieve of store schema definition
                return dataReader;
            }


            if (_WrappedCommand.CommandType == System.Data.CommandType.Text && !string.IsNullOrWhiteSpace(_WrappedCommand.CommandText))
            {

                #region Multiple commands (and @@guid)

                string[] commandTextList = _WrappedCommand.CommandText.Split(new string[] { ";\r\n" }, StringSplitOptions.None);
                if (commandTextList.Length > 1)
                {
                    dataReader = null;

                    // Set of commands
                    // The returned value will be the latest command result
                    foreach (string commandText in commandTextList)
                    {
                        if (string.IsNullOrWhiteSpace(commandText))
                            continue;

                        DbCommand command;
                        command = (DbCommand)((ICloneable)this._WrappedCommand).Clone();
                        int indexOfIdentity = commandText.ToLower().IndexOf("@@identity");
                        int indexOfGuid = commandText.ToLower().IndexOf("@@guid");
                        if (indexOfIdentity >= 0)
                        {
                            // Need to split again
                            command.CommandText = "Select @@identity";
                            object identity = command.ExecuteScalar();
                            int iIdentity = Convert.ToInt32(identity);
                            command = (DbCommand)((ICloneable)this._WrappedCommand).Clone();
                            command.CommandText = commandText.Remove(indexOfIdentity, 10).Insert(indexOfIdentity, iIdentity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else if (indexOfGuid >= 0)
                        {
                            // Need to split again
                            command.CommandText = "Select @@guid";
                            object identity = command.ExecuteScalar();
                            int iIdentity = Convert.ToInt32(identity);
                            command = (DbCommand)((ICloneable)this._WrappedCommand).Clone();
                            command.CommandText = commandText.Remove(indexOfGuid, 6).Insert(indexOfGuid, iIdentity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else
                            command.CommandText = commandText;

                        dataReader = command.ExecuteReader(behavior);
                    }
                    return new JetDataReader(dataReader);
                }
                #endregion


                #region TOP clause

                var indexOfTop = _WrappedCommand.CommandText.IndexOf(" top ", StringComparison.InvariantCultureIgnoreCase);
                int topCount = 0;
                if (indexOfTop != -1)
                {
                    int indexOfTopEnd = _WrappedCommand.CommandText.IndexOf(" ", indexOfTop + 5, StringComparison.InvariantCultureIgnoreCase);
                    string stringTopCount = _WrappedCommand.CommandText.Substring(indexOfTop + 5, indexOfTopEnd - indexOfTop - 5).Trim();
                    string[] stringTopCountElements = stringTopCount.Split('+');
                    int topCount0;
                    int topCount1;

                    if (stringTopCountElements[0].StartsWith("@"))
                        topCount0 = Convert.ToInt32(_WrappedCommand.Parameters[stringTopCountElements[0]].Value);
                    else if (!int.TryParse(stringTopCountElements[0], out topCount0))
                        throw new Exception("Invalid TOP clause parameter");

                    if (stringTopCountElements.Length == 1)
                        topCount1 = 0;
                    else if (stringTopCountElements[1].StartsWith("@"))
                        topCount1 = Convert.ToInt32(_WrappedCommand.Parameters[stringTopCountElements[1]].Value);
                    else if (!int.TryParse(stringTopCountElements[1], out topCount1))
                        throw new Exception("Invalid TOP clause parameter");

                    topCount = topCount0 + topCount1;
                    _WrappedCommand.CommandText = _WrappedCommand.CommandText.Remove(indexOfTop + 5, stringTopCount.Length).Insert(indexOfTop + 5, topCount.ToString());
                }

                #endregion

                #region SKIP clause

                var indexOfSkip = _WrappedCommand.CommandText.IndexOf(" skip ", StringComparison.InvariantCultureIgnoreCase);
                if (indexOfSkip != -1)
                {
                    string stringSkipCount = _WrappedCommand.CommandText.Substring(indexOfSkip + 5).Trim();
                    int skipCount;
                    if (stringSkipCount.StartsWith("@"))
                        skipCount = Convert.ToInt32(_WrappedCommand.Parameters[stringSkipCount].Value);
                    else if (!int.TryParse(stringSkipCount, out skipCount))
                        throw new Exception("Invalid SKIP clause parameter");

                    DbCommand command;
                    command = (DbCommand)((ICloneable)this._WrappedCommand).Clone();
                    command.CommandText = _WrappedCommand.CommandText.Remove(indexOfSkip);
                    return new JetDataReader(command.ExecuteReader(behavior), topCount - skipCount, skipCount);
                }

                #endregion

                if (topCount != 0)
                    return new JetDataReader(_WrappedCommand.ExecuteReader(behavior), topCount, 0);

            }



            return new JetDataReader(_WrappedCommand.ExecuteReader(behavior));
        }

        /// <summary>
        /// Executes the non query.
        /// </summary>
        /// <returns></returns>
        public override int ExecuteNonQuery()
        {
            ShowCommandText("ExecuteNonQuery");


            if (_WrappedCommand.CommandType == System.Data.CommandType.Text && !string.IsNullOrWhiteSpace(_WrappedCommand.CommandText))
            {
                string[] commandTextList = _WrappedCommand.CommandText.Split(new string[] { ";\r\n" }, StringSplitOptions.None);
                if (commandTextList.Length > 1)
                {
                    int result = 0;

                    // Set of commands
                    // The returned value will be the latest command result
                    foreach (string commandText in commandTextList)
                    {
                        if (string.IsNullOrWhiteSpace(commandText))
                            continue;

                        DbCommand command;
                        command = (DbCommand)((ICloneable)this._WrappedCommand).Clone();
                        int identityPosition = commandText.ToLower().IndexOf("@@identity");
                        if (identityPosition >= 0)
                        {
                            // Need to split again
                            command.CommandText = "Select @@identity";
                            int identity = Convert.ToInt32(command.ExecuteScalar());
                            command = (DbCommand)((ICloneable)this._WrappedCommand).Clone();
                            command.CommandText = commandText.Remove(identityPosition, 10).Insert(identityPosition, identity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else
                            command.CommandText = commandText;

                        result = command.ExecuteNonQuery();
                    }
                    return result;
                }
            }

            return this._WrappedCommand.ExecuteNonQuery();
        }

        /// <summary>
        /// Executes the query and returns the first column of the first row in the result set returned by the query. All other columns and rows are ignored
        /// </summary>
        /// <returns></returns>
        public override object ExecuteScalar()
        {
            ShowCommandText("ExecuteScalar");

            DbDataReader dataReader;

            if (JetStoreSchemaDefinitionRetrieve.TryGetDataReaderFromShowCommand(_WrappedCommand, out dataReader))
            {
                // Retrieve of store schema definition
                if (dataReader.HasRows)
                {
                    dataReader.Read();
                    return dataReader[0];
                }
                else
                    return DBNull.Value;
            }

            return this._WrappedCommand.ExecuteScalar();
        }

        private void ShowCommandText(string caller)
        {
            if (!JetConnection.ShowSqlStatements)
                return;

            Console.WriteLine("{0}==========\r\n{1}", caller, _WrappedCommand.CommandText);
            foreach (OleDbParameter parameter in _WrappedCommand.Parameters)
                Console.WriteLine("{0} = {1}", parameter.ParameterName, parameter.Value);
        }


        /// <summary>
        /// Creates a prepared (or compiled) version of the command on the data source
        /// </summary>
        public override void Prepare()
        {
            this._WrappedCommand.Prepare();
        }

        /// <summary>
        /// Gets or sets how command results are applied to the DataRow when used by the Update method of a DbDataAdapter.
        /// </summary>
        /// <value>
        /// The updated row source.
        /// </value>
        public override UpdateRowSource UpdatedRowSource
        {
            get
            {
                return this._WrappedCommand.UpdatedRowSource;
            }
            set
            {
                this._WrappedCommand.UpdatedRowSource = value;
            }
        }

        public static implicit operator OleDbCommand(JetCommand command)
        {
            return (OleDbCommand)command._WrappedCommand;
        }


        /// <summary>
        /// Clones this instance.
        /// </summary>
        /// <returns>The created object</returns>
        object ICloneable.Clone()
        {
            JetCommand clone = new JetCommand();
            clone._Connection = this._Connection;

            clone._WrappedCommand = (DbCommand)((ICloneable)this._WrappedCommand).Clone();

            return clone;
        }

    }
}
