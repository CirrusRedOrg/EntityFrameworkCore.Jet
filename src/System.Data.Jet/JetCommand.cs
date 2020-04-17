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

        private static readonly Regex _topRegularExpression = new Regex(@"(?<=(?:^|\s)select\s+top\s+)(?:\d+|(?:@\w+)|\?)(?=\s)", RegexOptions.IgnoreCase);
        private static readonly Regex _selectRowCountRegularExpression = new Regex(@"^\s*select\s*@@rowcount\s*;?\s*$", RegexOptions.IgnoreCase);
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
            var newCommandText = ApplyTopParameters(commandText);
            
            SortParameters(newCommandText, InnerCommand.Parameters);
            FixParameters(InnerCommand.Parameters);

            var command = (DbCommand) ((ICloneable) InnerCommand).Clone();
            command.CommandText = newCommandText;

            var dataReader = new JetDataReader(command.ExecuteReader(behavior));

            _rowCount = dataReader.RecordsAffected;

            return dataReader;
        }

        private int InternalExecuteNonQuery(string commandText)
        {
            if (!CheckExists(commandText, out var newCommandText))
                return 0;
            
            newCommandText = ApplyTopParameters(newCommandText);

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

        private string ApplyTopParameters(string commandText)
        {
            // We inline all TOP clause parameters of all SELECT statements, because Jet does not support parameters
            // in TOP clauses.
            var lastCommandText = commandText;
            var parameters = InnerCommand.Parameters.Cast<DbParameter>().ToList();
            
            while ((commandText = _topRegularExpression.Replace(
                commandText,
                match => (IsParameter(match.Value)
                        ? Convert.ToInt32(GetOrExtractParameter(commandText, match.Value, match.Index, parameters).Value)
                        : int.Parse(match.Value))
                    .ToString(), 1)) != lastCommandText)
            {
                lastCommandText = commandText;
            }
            
            InnerCommand.Parameters.Clear();
            InnerCommand.Parameters.AddRange(parameters.ToArray());

            return commandText;
        }

        protected virtual bool IsParameter(string fragment)
            => fragment.StartsWith("@") ||
               fragment.Equals("?");

        protected virtual DbParameter GetOrExtractParameter(string commandText, string name, int count, List<DbParameter> parameters)
        {
            if (name.Equals("?"))
            {
                var index = GetOdbcParameterCount(commandText.Substring(0, count));
                var parameter = InnerCommand.Parameters[index];
                
                parameters.RemoveAt(index);
                
                return parameter;
            }

            return InnerCommand.Parameters[name];
        }

        private static int GetOdbcParameterCount(string sqlFragment)
        {
            var parameterCount = 0;
            
            // We use '\0' as the default state and char.
            var state = '\0';
            var lastChar = '\0';

            // State machine to count ODBC parameter occurrences.
            foreach (var c in sqlFragment)
            {
                if (state == '\'')
                {
                    // We are currently inside a string, or closed the string in the last iteration but didn't
                    // know that at the time, because it still could have been the beginning of an escape sequence.

                    if (c == '\'')
                    {
                        // We either end the string, begin an escape sequence or end an escape sequence.
                        if (lastChar == '\'')
                        {
                            // This is the end of an escape sequence.
                            // We continue being in a string.
                            lastChar = '\0';
                        }
                        else
                        {
                            // This is either the beginning of an escape sequence, or the end of the string.
                            // We will know the in the next iteration.
                            lastChar = '\'';
                        }
                    }
                    else if (lastChar == '\'')
                    {
                        // The last iteration was the end of as string.
                        // Reset the current state and continue processing the current char.
                        state = '\0';
                        lastChar = '\0';
                    }
                }

                if (state == '"')
                {
                    // We are currently inside a string, or closed the string in the last iteration but didn't
                    // know that at the time, because it still could have been the beginning of an escape sequence.

                    if (c == '"')
                    {
                        // We either end the string, begin an escape sequence or end an escape sequence.
                        if (lastChar == '"')
                        {
                            // This is the end of an escape sequence.
                            // We continue being in a string.
                            lastChar = '\0';
                        }
                        else
                        {
                            // This is either the beginning of an escape sequence, or the end of the string.
                            // We will know the in the next iteration.
                            lastChar = '"';
                        }
                    }
                    else if (lastChar == '"')
                    {
                        // The last iteration was the end of as string.
                        // Reset the current state and continue processing the current char.
                        state = '\0';
                        lastChar = '\0';
                    }
                }

                if (state == '\0')
                {
                    if (c == '"')
                    {
                        state = '"';
                    }
                    else if (c == '\'')
                    {
                        state = '\'';
                    }
                    else if (c == '`')
                    {
                        state = '`';
                    }
                    else if (c == '?')
                    {
                        parameterCount++;
                    }
                }

                if (state == '`' &&
                    c == '`')
                {
                    state = '\0';
                }
            }

            return parameterCount;
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