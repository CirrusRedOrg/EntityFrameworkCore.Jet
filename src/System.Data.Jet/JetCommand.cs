using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.Jet.JetStoreSchemaDefinition;
using System.Data.OleDb;
using System.Linq;
using System.Text.RegularExpressions;

namespace System.Data.Jet
{
    public class JetCommand : DbCommand, ICloneable
    {
        private DbCommand _WrappedCommand;
        private JetConnection _Connection;
        private JetTransaction _Transaction;
        private bool _DesignTimeVisible;

        private Guid? _lastGuid = null;
        private int? _rowCount = null;

        private static readonly Regex _skipRegularExpression = new Regex(@"\bskip\s(?<stringSkipCount>@.*)\b", RegexOptions.IgnoreCase);
        private static readonly Regex _selectRowCountRegularExpression = new Regex(@"^\s*select\s*@@rowcount\s*[;]?\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex _ifStatementRegex = new Regex(@"^\s*if\s*(?<not>not)?\s*exists\s*\((?<sqlCheckCommand>.+)\)\s*then\s*(?<sqlCommand>.*)$", RegexOptions.IgnoreCase);

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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _WrappedCommand.Dispose();

            base.Dispose(disposing);
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
            get { return this._WrappedCommand.CommandText; }
            set { this._WrappedCommand.CommandText = value; }
        }

        /// <summary>
        /// Gets or sets the command timeout.
        /// </summary>
        /// <value>
        /// The command timeout.
        /// </value>
        public override int CommandTimeout
        {
            get { return this._WrappedCommand.CommandTimeout; }
            set { this._WrappedCommand.CommandTimeout = value; }
        }

        /// <summary>
        /// Gets or sets the type of the command.
        /// </summary>
        /// <value>
        /// The type of the command.
        /// </value>
        public override CommandType CommandType
        {
            get { return this._WrappedCommand.CommandType; }
            set { this._WrappedCommand.CommandType = value; }
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
            get { return this._Connection; }
            set
            {
                if (value == null)
                {
                    this._Connection = null;
                }
                else
                {
                    if (!typeof(JetConnection).IsAssignableFrom(value.GetType()))
                        throw new InvalidOperationException("The JetCommand connection should be a JetConnection");

                    this._Connection = (JetConnection) value;
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
            get { return _Transaction; }
            set { _Transaction = (JetTransaction) value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether is design time visible.
        /// </summary>
        /// <value>
        ///   <c>true</c> if design time visible; otherwise, <c>false</c>.
        /// </value>
        public override bool DesignTimeVisible
        {
            get { return this._DesignTimeVisible; }
            set { this._DesignTimeVisible = value; }
        }

        /// <summary>
        /// Executes the database data reader.
        /// </summary>
        /// <param name="behavior">The behavior.</param>
        /// <returns></returns>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            if (Connection == null)
                throw new InvalidOperationException(Messages.PropertyNotInitialized(nameof(Connection)));

            if (Connection.State != ConnectionState.Open)
                throw new InvalidOperationException(Messages.CannotCallMethodInThisConnectionState("ExecuteReader", ConnectionState.Open, Connection.State));

            _WrappedCommand.Connection = _Connection.InnerConnection;

            // OLE DB forces us to use an existing active transaction, if one is available.
            _WrappedCommand.Transaction = _Transaction?.WrappedTransaction ?? _Connection.ActiveTransaction?.WrappedTransaction;

            LogHelper.ShowCommandText("ExecuteDbDataReader", _WrappedCommand);

            DbDataReader dataReader;
            if (JetStoreSchemaDefinitionRetrieve.TryGetDataReaderFromShowCommand(_WrappedCommand, out dataReader))
                // Retrieve of store schema definition
                return dataReader;

            if (_WrappedCommand.CommandType != CommandType.Text)
                return new JetDataReader(_WrappedCommand.ExecuteReader(behavior));

            string[] commandTextList = SplitCommands(_WrappedCommand.CommandText);

            dataReader = null;
            for (int i = 0; i < commandTextList.Length; i++)
            {
                string commandText = commandTextList[i];
                if ((dataReader = TryGetDataReaderForSelectRowCount(commandText)) != null)
                    continue;

                commandText = ParseIdentity(commandText);
                commandText = ParseGuid(commandText);

                dataReader = InternalExecuteDbDataReader(commandText, behavior);
            }

            return dataReader;
        }

        private DbDataReader TryGetDataReaderForSelectRowCount(string commandText)
        {
            if (_selectRowCountRegularExpression.Match(commandText)
                .Success)
            {
                if (_rowCount == null)
                    throw new InvalidOperationException("Invalid " + commandText + ". Run a DataReader before.");
                DataTable dataTable = new DataTable("Rowcount");
                dataTable.Columns.Add("ROWCOUNT", typeof(int));
                dataTable.Rows.Add(_rowCount.Value);
                return new DataTableReader(dataTable);
            }

            return null;
        }

        /// <summary>
        /// Executes the non query.
        /// </summary>
        /// <returns></returns>
        public override int ExecuteNonQuery()
        {
            if (Connection == null)
                throw new InvalidOperationException(Messages.PropertyNotInitialized(nameof(Connection)));

            LogHelper.ShowCommandText("ExecuteNonQuery", _WrappedCommand);

            if (JetStoreDatabaseHandling.TryDatabaseOperation(_WrappedCommand.CommandText))
                return 1;
            if (JetRenameHandling.TryDatabaseOperation(Connection.ConnectionString, _WrappedCommand.CommandText))
                return 1;

            if (Connection.State != ConnectionState.Open)
                throw new InvalidOperationException(Messages.CannotCallMethodInThisConnectionState(nameof(ExecuteNonQuery), ConnectionState.Open, Connection.State));

            _WrappedCommand.Connection = _Connection.InnerConnection;

            // OLE DB forces us to use an existing active transaction, if one is available.
            _WrappedCommand.Transaction = _Transaction?.WrappedTransaction ?? _Connection.ActiveTransaction?.WrappedTransaction;

            if (_WrappedCommand.CommandType != CommandType.Text)
                return _WrappedCommand.ExecuteNonQuery();

            string[] commandTextList = SplitCommands(_WrappedCommand.CommandText);

            int returnValue = -1;
            for (int i = 0; i < commandTextList.Length; i++)
            {
                string commandText = commandTextList[i];
                if (_selectRowCountRegularExpression.Match(commandText)
                    .Success)
                {
                    if (_rowCount == null)
                        throw new InvalidOperationException("Invalid " + commandText + ". Run a DataReader before.");
                    returnValue = _rowCount.Value;
                    continue;
                }

                commandText = ParseIdentity(commandText);
                commandText = ParseGuid(commandText);

                returnValue = InternalExecuteNonQuery(commandText);
            }

            return returnValue;
        }

        /// <summary>
        /// Executes the query and returns the first column of the first row in the result set returned by the query. All other columns and rows are ignored
        /// </summary>
        /// <returns></returns>
        public override object ExecuteScalar()
        {
            if (Connection == null)
                throw new InvalidOperationException(Messages.PropertyNotInitialized(nameof(Connection)));

            if (Connection.State != ConnectionState.Open)
                throw new InvalidOperationException(Messages.CannotCallMethodInThisConnectionState(nameof(ExecuteScalar), ConnectionState.Open, Connection.State));

            _WrappedCommand.Connection = _Connection.InnerConnection;

            // OLE DB forces us to use an existing active transaction, if one is available.
            _WrappedCommand.Transaction = _Transaction?.WrappedTransaction ?? _Connection.ActiveTransaction?.WrappedTransaction;

            LogHelper.ShowCommandText("ExecuteScalar", _WrappedCommand);

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

        private JetDataReader InternalExecuteDbDataReader(string commandText, CommandBehavior behavior)
        {
            int topCount;
            int skipCount;
            string newCommandText;
            ParseSkipTop(commandText, out topCount, out skipCount, out newCommandText);
            SortParameters(newCommandText, _WrappedCommand.Parameters);
            FixParameters(_WrappedCommand.Parameters);

            DbCommand command;
            command = (DbCommand) ((ICloneable) this._WrappedCommand).Clone();
            command.CommandText = newCommandText;

            JetDataReader dataReader;

            if (skipCount != 0)
                dataReader = new JetDataReader(
                    command.ExecuteReader(behavior), topCount == -1
                        ? 0
                        : topCount - skipCount, skipCount);
            else if (topCount >= 0)
                dataReader = new JetDataReader(command.ExecuteReader(behavior), topCount, 0);
            else
                dataReader = new JetDataReader(command.ExecuteReader(behavior));

            _rowCount = dataReader.RecordsAffected;

            return dataReader;
        }

        private int InternalExecuteNonQuery(string commandText)
        {
            // ReSharper disable NotAccessedVariable
            int topCount;
            int skipCount;
            // ReSharper restore NotAccessedVariable
            string newCommandText;
            if (!CheckExists(commandText, out newCommandText))
                return 0;
            ParseSkipTop(newCommandText, out topCount, out skipCount, out newCommandText);

            SortParameters(newCommandText, _WrappedCommand.Parameters);
            FixParameters(_WrappedCommand.Parameters);

            DbCommand command;
            command = (DbCommand) ((ICloneable) this._WrappedCommand).Clone();
            command.CommandText = newCommandText;

            _rowCount = command.ExecuteNonQuery();

            return _rowCount.Value;
        }

        private bool CheckExists(string commandText, out string newCommandText)
        {
            Match match = _ifStatementRegex.Match(commandText);
            newCommandText = commandText;
            if (!match.Success)
                return true;

            string not = match.Groups["not"]
                .Value;
            string sqlCheckCommand = match.Groups["sqlCheckCommand"]
                .Value;
            newCommandText = match.Groups["sqlCommand"]
                .Value;

            bool hasRows;
            using (JetCommand command = (JetCommand) ((ICloneable) this).Clone())
            {
                command.CommandText = sqlCheckCommand;
                using (var reader = command.ExecuteReader())
                {
                    hasRows = reader.HasRows;
                }
            }

            if (!string.IsNullOrWhiteSpace(not))
                return !hasRows;
            else
                return hasRows;
        }

        private void FixParameters(DbParameterCollection parameters)
        {
            if (parameters.Count == 0)
                return;
            foreach (OleDbParameter parameter in parameters)
            {
                if (parameter.Value is TimeSpan)
                    parameter.Value = JetConfiguration.TimeSpanOffset + (TimeSpan) parameter.Value;
                if (parameter.Value is DateTime)
                {
                    // Hack: https://github.com/fsprojects/SQLProvider/issues/191
                    DateTime dt = (DateTime) parameter.Value;
                    parameter.Value = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Kind);
                }
            }
        }

        private void SortParameters(string query, DbParameterCollection parameters)
        {
            if (parameters.Count == 0)
                return;

            var parameterArray = parameters.Cast<OleDbParameter>()
                .ToArray();
            // ReSharper disable once CoVariantArrayConversion
            Array.Sort(parameterArray, new ParameterPositionComparer(query));

            parameters.Clear();
            foreach (OleDbParameter parameter in parameterArray)
                parameters.Add(new OleDbParameter(parameter.ParameterName, parameter.Value));
        }

        private class ParameterPositionComparer : IComparer<DbParameter>
        {
            private readonly string _query;

            public ParameterPositionComparer(string query)
            {
                _query = query;
            }

            public int Compare(DbParameter x, DbParameter y)
            {
                if (x == null)
                    throw new ArgumentNullException(nameof(x));
                if (y == null)
                    throw new ArgumentNullException(nameof(y));

                int xPosition = _query.IndexOf(x.ParameterName, StringComparison.Ordinal);
                int yPosition = _query.IndexOf(y.ParameterName, StringComparison.Ordinal);
                if (xPosition == -1)
                    xPosition = int.MaxValue;
                if (yPosition == -1)
                    yPosition = int.MaxValue;
                return xPosition.CompareTo(yPosition);
            }
        }

        private string[] SplitCommands(string command)
        {
            string[] commandParts =
                command.Replace("\r\n", "\n")
                    .Replace("\r", "\n")
                    .Split(new[] {";\n"}, StringSplitOptions.None);
            List<string> commands = new List<string>(commandParts.Length);
            foreach (string commandPart in commandParts)
            {
                if (!string.IsNullOrWhiteSpace(
                    commandPart.Replace("\n", "")
                        .Replace(";", "")))
                    commands.Add(commandPart);
            }

            return commands.ToArray();
        }

        private string ParseIdentity(string commandText)
        {
            if (commandText.ToLower()
                .Contains("@@identity"))
            {
                DbCommand command;
                command = (DbCommand) ((ICloneable) this._WrappedCommand).Clone();
                command.CommandText = "Select @@identity";
                object identity = command.ExecuteScalar();
                int iIdentity = Convert.ToInt32(identity);
                LogHelper.ShowInfo("@@identity = {0}", iIdentity);
                return Regex.Replace(commandText, "@@identity", iIdentity.ToString(System.Globalization.CultureInfo.InvariantCulture), RegexOptions.IgnoreCase);
            }

            return commandText;
        }

        private string ParseGuid(string commandText)
        {
            while (commandText.ToLower()
                .Contains("newguid()"))
            {
                _lastGuid = Guid.NewGuid();
                commandText = Regex.Replace(commandText, @"newguid\(\)", string.Format("{{{0}}}", _lastGuid), RegexOptions.IgnoreCase);
            }

            if (commandText.ToLower()
                .Contains("@@guid"))
            {
                LogHelper.ShowInfo("@@guid = {{{0}}}", _lastGuid);
                commandText = Regex.Replace(commandText, "@@guid", string.Format("{{{0}}}", _lastGuid), RegexOptions.IgnoreCase);
            }

            return commandText;
        }

        private void ParseSkipTop(string commandText, out int topCount, out int skipCount, out string newCommandText)
        {
            newCommandText = commandText;

            #region TOP clause

            topCount = -1;
            skipCount = 0;

            var indexOfTop = newCommandText.IndexOf(" top ", StringComparison.InvariantCultureIgnoreCase);
            while (indexOfTop != -1)
            {
                int indexOfTopEnd = newCommandText.IndexOf(" ", indexOfTop + 5, StringComparison.InvariantCultureIgnoreCase);
                string stringTopCount = newCommandText.Substring(indexOfTop + 5, indexOfTopEnd - indexOfTop - 5)
                    .Trim();
                string[] stringTopCountElements = stringTopCount.Split('+');
                int topCount0;
                int topCount1;

                if (stringTopCountElements[0]
                    .StartsWith("@"))
                    topCount0 = Convert.ToInt32(
                        _WrappedCommand.Parameters[stringTopCountElements[0]]
                            .Value);
                else if (!int.TryParse(stringTopCountElements[0], out topCount0))
                    throw new Exception("Invalid TOP clause parameter");

                if (stringTopCountElements.Length == 1)
                    topCount1 = 0;
                else if (stringTopCountElements[1]
                    .StartsWith("@"))
                    topCount1 = Convert.ToInt32(
                        _WrappedCommand.Parameters[stringTopCountElements[1]]
                            .Value);
                else if (!int.TryParse(stringTopCountElements[1], out topCount1))
                    throw new Exception("Invalid TOP clause parameter");

                int localTopCount = topCount0 + topCount1;
                newCommandText = newCommandText.Remove(indexOfTop + 5, stringTopCount.Length)
                    .Insert(indexOfTop + 5, localTopCount.ToString());
                if (indexOfTop <= 12)
                    topCount = localTopCount;
                indexOfTop = newCommandText.IndexOf(" top ", indexOfTop + 5, StringComparison.InvariantCultureIgnoreCase);
            }

            #endregion

            #region SKIP clause

            Match matchSkipRegularExpression = _skipRegularExpression.Match(newCommandText);
            if (matchSkipRegularExpression.Success)
            {
                string stringSkipCount;
                stringSkipCount = matchSkipRegularExpression.Groups["stringSkipCount"]
                    .Value;

                if (stringSkipCount.StartsWith("@"))
                    skipCount = Convert.ToInt32(
                        _WrappedCommand.Parameters[stringSkipCount]
                            .Value);
                else if (!int.TryParse(stringSkipCount, out skipCount))
                    throw new Exception("Invalid SKIP clause parameter");
                newCommandText = newCommandText.Remove(matchSkipRegularExpression.Index, matchSkipRegularExpression.Length);
            }

            #endregion
        }

        /// <summary>
        /// Creates a prepared (or compiled) version of the command on the data source
        /// </summary>
        public override void Prepare()
        {
            if (Connection == null)
                throw new InvalidOperationException(Messages.PropertyNotInitialized(nameof(Connection)));

            if (Connection.State != ConnectionState.Open)
                throw new InvalidOperationException(Messages.CannotCallMethodInThisConnectionState(nameof(Prepare), ConnectionState.Open, Connection.State));

            _WrappedCommand.Connection = _Connection.InnerConnection;

            // OLE DB forces us to use an existing active transaction, if one is available.
            _WrappedCommand.Transaction = _Transaction?.WrappedTransaction ?? _Connection.ActiveTransaction?.WrappedTransaction;

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
            get { return this._WrappedCommand.UpdatedRowSource; }
            set { this._WrappedCommand.UpdatedRowSource = value; }
        }

        public static implicit operator OleDbCommand(JetCommand command)
        {
            return (OleDbCommand) command._WrappedCommand;
        }

        /// <summary>
        /// Clones this instance.
        /// </summary>
        /// <returns>The created object</returns>
        object ICloneable.Clone()
        {
            JetCommand clone = new JetCommand();
            clone._Connection = this._Connection;

            clone._WrappedCommand = (DbCommand) ((ICloneable) this._WrappedCommand).Clone();

            return clone;
        }
    }
}