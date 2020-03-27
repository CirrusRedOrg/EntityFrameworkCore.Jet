using System.Collections.Generic;
using System.Data.Common;
using System.Data.Jet.JetStoreSchemaDefinition;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace System.Data.Jet
{
    public class JetCommand : DbCommand, ICloneable
    {
#if DEBUG
        private static int _activeObjectsCount;
#endif
        private readonly JetConnection _connection;
        private JetTransaction _transaction;

        private Guid? _lastGuid;
        private int? _rowCount;

        private static readonly Regex _skipRegularExpression = new Regex(@"\bskip\s(?<stringSkipCount>@.*)\b", RegexOptions.IgnoreCase);
        private static readonly Regex _selectRowCountRegularExpression = new Regex(@"^\s*select\s*@@rowcount\s*[;]?\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex _ifStatementRegex = new Regex(@"^\s*if\s*(?<not>not)?\s*exists\s*\((?<sqlCheckCommand>.+)\)\s*then\s*(?<sqlCommand>.*)$", RegexOptions.IgnoreCase);

        protected JetCommand(JetCommand source)
        {
#if DEBUG
            Interlocked.Increment(ref _activeObjectsCount);
#endif
            _connection = source._connection;
            _transaction = source._transaction;
            InnerCommand = source.InnerCommand;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JetCommand"/> class.
        /// </summary>
        /// <param name="commandText">The command text.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="transaction">The transaction.</param>
        internal JetCommand(JetConnection connection, string commandText = null, JetTransaction transaction = null)
        {
#if DEBUG
            Interlocked.Increment(ref _activeObjectsCount);
#endif
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _transaction = transaction;

            InnerCommand = connection.JetFactory.InnerFactory.CreateCommand();
            InnerCommand.CommandText = commandText;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                InnerCommand.Dispose();

            base.Dispose(disposing);

#if DEBUG
            Interlocked.Decrement(ref _activeObjectsCount);
#endif
        }

        internal DbCommand InnerCommand { get; }

        /// <summary>
        /// Attempts to Cancels the command execution
        /// </summary>
        public override void Cancel()
            => InnerCommand.Cancel();

        /// <summary>
        /// Gets or sets the command text.
        /// </summary>
        /// <value>
        /// The command text.
        /// </value>
        public override string CommandText
        {
            get => InnerCommand.CommandText;
            set => InnerCommand.CommandText = value;
        }

        /// <summary>
        /// Gets or sets the command timeout.
        /// </summary>
        /// <value>
        /// The command timeout.
        /// </value>
        public override int CommandTimeout
        {
            get => InnerCommand.CommandTimeout;
            set => InnerCommand.CommandTimeout = value;
        }

        /// <summary>
        /// Gets or sets the type of the command.
        /// </summary>
        /// <value>
        /// The type of the command.
        /// </value>
        public override CommandType CommandType
        {
            get => InnerCommand.CommandType;
            set => InnerCommand.CommandType = value;
        }

        /// <summary>
        /// Creates the database parameter.
        /// </summary>
        /// <returns></returns>
        protected override DbParameter CreateDbParameter()
            => InnerCommand.CreateParameter();

        /// <summary>
        /// Gets or sets the database connection.
        /// </summary>
        /// <value>
        /// The database connection.
        /// </value>
        protected override DbConnection DbConnection
        {
            get => _connection;
            set
            {
                if (value != _connection)
                    throw new NotSupportedException($"The {DbConnection} property cannot be changed.");
            }
        }

        /// <summary>
        /// Gets the database parameter collection.
        /// </summary>
        /// <value>
        /// The database parameter collection.
        /// </value>
        protected override DbParameterCollection DbParameterCollection
            => InnerCommand.Parameters;

        /// <summary>
        /// Gets or sets the database transaction.
        /// </summary>
        /// <value>
        /// The database transaction.
        /// </value>
        protected override DbTransaction DbTransaction
        {
            get => _transaction;
            set => _transaction = (JetTransaction) value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether is design time visible.
        /// </summary>
        /// <value>
        ///   <c>true</c> if design time visible; otherwise, <c>false</c>.
        /// </value>
        public override bool DesignTimeVisible { get; set; }

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

            InnerCommand.Connection = _connection.InnerConnection;

            // OLE DB forces us to use an existing active transaction, if one is available.
            InnerCommand.Transaction = _transaction?.WrappedTransaction ?? _connection.ActiveTransaction?.WrappedTransaction;

            LogHelper.ShowCommandText("ExecuteDbDataReader", InnerCommand);

            if (JetStoreSchemaDefinitionRetrieve.TryGetDataReaderFromShowCommand(InnerCommand, _connection.JetFactory.InnerFactory, out var dataReader))
                // Retrieve of store schema definition
                return dataReader;

            if (InnerCommand.CommandType != CommandType.Text)
                return new JetDataReader(InnerCommand.ExecuteReader(behavior));

            var commandTextList = SplitCommands(InnerCommand.CommandText);

            dataReader = null;
            foreach (var t in commandTextList)
            {
                var commandText = t;
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
                var dataTable = new DataTable("Rowcount");
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

            LogHelper.ShowCommandText("ExecuteNonQuery", InnerCommand);

            if (JetStoreDatabaseHandling.TryDatabaseOperation(this))
                return 1;
            if (JetRenameHandling.TryDatabaseOperation(Connection.ConnectionString, InnerCommand.CommandText))
                return 1;

            if (Connection.State != ConnectionState.Open)
                throw new InvalidOperationException(Messages.CannotCallMethodInThisConnectionState(nameof(ExecuteNonQuery), ConnectionState.Open, Connection.State));

            InnerCommand.Connection = _connection.InnerConnection;

            // OLE DB forces us to use an existing active transaction, if one is available.
            InnerCommand.Transaction = _transaction?.WrappedTransaction ?? _connection.ActiveTransaction?.WrappedTransaction;

            if (InnerCommand.CommandType != CommandType.Text)
                return InnerCommand.ExecuteNonQuery();

            var commandTextList = SplitCommands(InnerCommand.CommandText);

            var returnValue = -1;
            foreach (var t in commandTextList)
            {
                var commandText = t;
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

            InnerCommand.Connection = _connection.InnerConnection;

            // OLE DB forces us to use an existing active transaction, if one is available.
            InnerCommand.Transaction = _transaction?.WrappedTransaction ?? _connection.ActiveTransaction?.WrappedTransaction;

            LogHelper.ShowCommandText("ExecuteScalar", InnerCommand);

            if (JetStoreSchemaDefinitionRetrieve.TryGetDataReaderFromShowCommand(InnerCommand, _connection.JetFactory.InnerFactory, out var dataReader))
            {
                // Retrieve of store schema definition
                if (dataReader.HasRows)
                {
                    dataReader.Read();
                    return dataReader[0];
                }

                return DBNull.Value;
            }

            return InnerCommand.ExecuteScalar();
        }

        private JetDataReader InternalExecuteDbDataReader(string commandText, CommandBehavior behavior)
        {
            ParseSkipTop(commandText, out var topCount, out var skipCount, out var newCommandText);
            SortParameters(newCommandText, InnerCommand.Parameters);
            FixParameters(InnerCommand.Parameters);

            var command = (DbCommand) ((ICloneable) InnerCommand).Clone();
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
            // ReSharper restore NotAccessedVariable
            if (!CheckExists(commandText, out var newCommandText))
                return 0;
            ParseSkipTop(newCommandText, out var topCount, out var skipCount, out newCommandText);

            SortParameters(newCommandText, InnerCommand.Parameters);
            FixParameters(InnerCommand.Parameters);

            var command = (DbCommand) ((ICloneable) InnerCommand).Clone();
            command.CommandText = newCommandText;

            _rowCount = command.ExecuteNonQuery();

            return _rowCount.Value;
        }

        private bool CheckExists(string commandText, out string newCommandText)
        {
            var match = _ifStatementRegex.Match(commandText);
            newCommandText = commandText;
            if (!match.Success)
                return true;

            var not = match.Groups["not"]
                .Value;
            var sqlCheckCommand = match.Groups["sqlCheckCommand"]
                .Value;
            newCommandText = match.Groups["sqlCommand"]
                .Value;

            bool hasRows;
            using (var command = (JetCommand) ((ICloneable) this).Clone())
            {
                command.CommandText = sqlCheckCommand;
                using (var reader = command.ExecuteReader())
                {
                    hasRows = reader.HasRows;
                }
            }

            if (!string.IsNullOrWhiteSpace(not))
                return !hasRows;
            return hasRows;
        }

        private void FixParameters(DbParameterCollection parameters)
        {
            if (parameters.Count == 0)
                return;
            foreach (DbParameter parameter in parameters)
            {
                if (parameter.Value is TimeSpan ts)
                    parameter.Value = JetConfiguration.TimeSpanOffset + ts;
                if (parameter.Value is DateTime dt)
                {
                    // Hack: https://github.com/fsprojects/SQLProvider/issues/191
                    parameter.Value = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Kind);
                }
            }
        }

        private void SortParameters(string query, DbParameterCollection parameters)
        {
            if (parameters.Count == 0)
                return;

            var parameterArray = parameters.Cast<DbParameter>()
                .OrderBy(p => p, new ParameterPositionComparer(query))
                .ToArray();

            parameters.Clear();
            parameters.AddRange(parameterArray);
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

                var xPosition = _query.IndexOf(x.ParameterName, StringComparison.Ordinal);
                var yPosition = _query.IndexOf(y.ParameterName, StringComparison.Ordinal);
                if (xPosition == -1)
                    xPosition = int.MaxValue;
                if (yPosition == -1)
                    yPosition = int.MaxValue;
                return xPosition.CompareTo(yPosition);
            }
        }

        private string[] SplitCommands(string command)
        {
            var commandParts =
                command.Replace("\r\n", "\n")
                    .Replace("\r", "\n")
                    .Split(new[] {";\n"}, StringSplitOptions.None);
            var commands = new List<string>(commandParts.Length);
            foreach (var commandPart in commandParts)
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
                command = (DbCommand) ((ICloneable) InnerCommand).Clone();
                command.CommandText = "Select @@identity";
                var identity = command.ExecuteScalar();
                var iIdentity = Convert.ToInt32(identity);
                LogHelper.ShowInfo("@@identity = {0}", iIdentity);
                return Regex.Replace(commandText, "@@identity", iIdentity.ToString(Globalization.CultureInfo.InvariantCulture), RegexOptions.IgnoreCase);
            }

            return commandText;
        }

        private string ParseGuid(string commandText)
        {
            while (commandText.ToLower()
                .Contains("newguid()"))
            {
                _lastGuid = Guid.NewGuid();
                commandText = Regex.Replace(commandText, @"newguid\(\)", $"{{{_lastGuid}}}", RegexOptions.IgnoreCase);
            }

            if (commandText.ToLower()
                .Contains("@@guid"))
            {
                LogHelper.ShowInfo("@@guid = {{{0}}}", _lastGuid);
                commandText = Regex.Replace(commandText, "@@guid", $"{{{_lastGuid}}}", RegexOptions.IgnoreCase);
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
                var indexOfTopEnd = newCommandText.IndexOf(" ", indexOfTop + 5, StringComparison.InvariantCultureIgnoreCase);
                var stringTopCount = newCommandText.Substring(indexOfTop + 5, indexOfTopEnd - indexOfTop - 5)
                    .Trim();
                var stringTopCountElements = stringTopCount.Split('+');
                int topCount0;
                int topCount1;

                if (stringTopCountElements[0]
                    .StartsWith("@"))
                    topCount0 = Convert.ToInt32(
                        InnerCommand.Parameters[stringTopCountElements[0]]
                            .Value);
                else if (!int.TryParse(stringTopCountElements[0], out topCount0))
                    throw new Exception("Invalid TOP clause parameter");

                if (stringTopCountElements.Length == 1)
                    topCount1 = 0;
                else if (stringTopCountElements[1]
                    .StartsWith("@"))
                    topCount1 = Convert.ToInt32(
                        InnerCommand.Parameters[stringTopCountElements[1]]
                            .Value);
                else if (!int.TryParse(stringTopCountElements[1], out topCount1))
                    throw new Exception("Invalid TOP clause parameter");

                var localTopCount = topCount0 + topCount1;
                newCommandText = newCommandText.Remove(indexOfTop + 5, stringTopCount.Length)
                    .Insert(indexOfTop + 5, localTopCount.ToString());
                if (indexOfTop <= 12)
                    topCount = localTopCount;
                indexOfTop = newCommandText.IndexOf(" top ", indexOfTop + 5, StringComparison.InvariantCultureIgnoreCase);
            }

            #endregion

            #region SKIP clause

            var matchSkipRegularExpression = _skipRegularExpression.Match(newCommandText);
            if (matchSkipRegularExpression.Success)
            {
                var stringSkipCount = matchSkipRegularExpression.Groups["stringSkipCount"]
                    .Value;

                if (stringSkipCount.StartsWith("@"))
                    skipCount = Convert.ToInt32(
                        InnerCommand.Parameters[stringSkipCount]
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

            InnerCommand.Connection = _connection.InnerConnection;

            // OLE DB forces us to use an existing active transaction, if one is available.
            InnerCommand.Transaction = _transaction?.WrappedTransaction ?? _connection.ActiveTransaction?.WrappedTransaction;

            InnerCommand.Prepare();
        }

        /// <summary>
        /// Gets or sets how command results are applied to the DataRow when used by the Update method of a DbDataAdapter.
        /// </summary>
        /// <value>
        /// The updated row source.
        /// </value>
        public override UpdateRowSource UpdatedRowSource
        {
            get => InnerCommand.UpdatedRowSource;
            set => InnerCommand.UpdatedRowSource = value;
        }

        /// <summary>
        /// Clones this instance.
        /// </summary>
        /// <returns>The created object</returns>
        object ICloneable.Clone()
            => new JetCommand(this);
    }
}