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

        private static readonly Regex _createProcedureExpression = new Regex(@"^\s*create\s*procedure\b", RegexOptions.IgnoreCase);
        private static readonly Regex _topParameterRegularExpression = new Regex(@"(?<=(?:^|\s)select\s+top\s+)(?:@\w+|\?)(?=\s)", RegexOptions.IgnoreCase);
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

            ExpandParameters();
            
            LogHelper.ShowCommandText("ExecuteDbDataReader", InnerCommand);

            if (JetStoreSchemaDefinitionRetrieve.TryGetDataReaderFromShowCommand(InnerCommand, _connection.JetFactory.InnerFactory, out var dataReader))
                // Retrieve of store schema definition
                return dataReader;

            if (InnerCommand.CommandType != CommandType.Text)
                return new JetDataReader(InnerCommand.ExecuteReader(behavior));

            if ((dataReader = TryGetDataReaderForSelectRowCount(InnerCommand.CommandText)) == null)
            {
                InnerCommand.CommandText = ParseIdentity(InnerCommand.CommandText);
                InnerCommand.CommandText = ParseGuid(InnerCommand.CommandText);

                InlineTopParameters();
                FixParameters();

                dataReader = new JetDataReader(InnerCommand.ExecuteReader(behavior));

                _rowCount = dataReader.RecordsAffected;
            }

            return dataReader;
        }

        private DbDataReader TryGetDataReaderForSelectRowCount(string commandText)
        {
            if (_selectRowCountRegularExpression.Match(commandText).Success)
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
            
            ExpandParameters();

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
            
            if (_selectRowCountRegularExpression.Match(InnerCommand.CommandText)
                .Success)
            {
                // TODO: Fix exception message.
                if (_rowCount == null)
                    throw new InvalidOperationException("Invalid " + InnerCommand.CommandText + ". Run a DataReader before.");
                return _rowCount.Value;
            }

            InnerCommand.CommandText = ParseIdentity(InnerCommand.CommandText);
            InnerCommand.CommandText = ParseGuid(InnerCommand.CommandText);

            if (!CheckExists(InnerCommand.CommandText, out var newCommandText))
                return 0;

            InnerCommand.CommandText = newCommandText;
            
            InlineTopParameters();
            FixParameters();

            _rowCount = InnerCommand.ExecuteNonQuery();

            return _rowCount.Value;
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

            ExpandParameters();
            
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

            InlineTopParameters();
            FixParameters();

            return InnerCommand.ExecuteScalar();
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

        private void FixParameters()
        {
            var parameters = InnerCommand.Parameters;
            
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

        private string ParseIdentity(string commandText)
        {
            // TODO: Fix the following code, that does work only for common scenarios. Use state machine instead.
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
            // TODO: Fix the following code, that does work only for common scenarios. Use state machine instead.
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

        private void InlineTopParameters()
        {
            // We inline all TOP clause parameters of all SELECT statements, because Jet does not support parameters
            // in TOP clauses.
            var parameters = InnerCommand.Parameters.Cast<DbParameter>().ToList();

            if (parameters.Count > 0)
            {
                var lastCommandText = InnerCommand.CommandText;
                var commandText = lastCommandText;

                while ((commandText = _topParameterRegularExpression.Replace(
                    lastCommandText,
                    match => Convert.ToInt32(ExtractParameter(commandText, match.Value, match.Index, parameters).Value).ToString(),
                    1)) != lastCommandText)
                {
                    lastCommandText = commandText;
                }

                InnerCommand.CommandText = commandText;

                InnerCommand.Parameters.Clear();
                InnerCommand.Parameters.AddRange(parameters.ToArray());
            }
        }

        protected virtual bool IsParameter(string fragment)
            => fragment.StartsWith("@") ||
               fragment.Equals("?");

        protected virtual DbParameter ExtractParameter(string commandText, string name, int count, List<DbParameter> parameters)
        {
            var indices = GetParameterIndices(commandText.Substring(0, count));
            var parameter = InnerCommand.Parameters[indices.Count];
            
            parameters.RemoveAt(indices.Count);
            
            return parameter;
        }

        protected virtual void ExpandParameters()
        {
            if (_createProcedureExpression.IsMatch(InnerCommand.CommandText))
            {
                return;
            }
            
            var indices = GetParameterIndices(InnerCommand.CommandText);

            if (indices.Count <= 0)
            {
                return;
            }

            var placeholders = GetParameterPlaceholders(InnerCommand.CommandText, indices);
            
            if (placeholders.All(t => t.Name.StartsWith("@")))
            {
                MatchParametersAndPlaceholders(placeholders);

                if (JetConnection.GetDataAccessProviderType(_connection.DataAccessProviderFactory) == DataAccessProviderType.Odbc)
                {
                    foreach (var placeholder in placeholders.Reverse())
                    {
                        InnerCommand.CommandText = InnerCommand.CommandText
                            .Remove(placeholder.Index, placeholder.Name.Length)
                            .Insert(placeholder.Index, "?");
                    }
                }
                
                InnerCommand.Parameters.Clear();
                InnerCommand.Parameters.AddRange(placeholders.Select(p => p.Parameter).ToArray());
            }
            else if (placeholders.All(t => t.Name == "?"))
            {
                throw new InvalidOperationException("Parameter placeholder count does not match parameter count.");
            }
            else
            {
                throw new InvalidOperationException("Inconsistent parameter placeholder naming used.");
            }
        }

        protected virtual void MatchParametersAndPlaceholders(IReadOnlyList<ParameterPlaceholder> placeholders)
        {
            var unusedParameters = InnerCommand.Parameters
                .Cast<DbParameter>()
                .ToList();

            foreach (var placeholder in placeholders)
            {
                var parameter = unusedParameters
                    .FirstOrDefault(p => placeholder.Name.Equals(p.ParameterName, StringComparison.Ordinal));

                if (parameter != null)
                {
                    placeholder.Parameter = parameter;
                    unusedParameters.Remove(parameter);
                }
                else
                {
                    parameter = placeholders
                        .FirstOrDefault(p => placeholder.Name.Equals(p.Name, StringComparison.Ordinal))
                        ?.Parameter;

                    if (parameter == null)
                    {
                        throw new InvalidOperationException($"Cannot find parameter with same name as parameter placeholder \"{placeholder.Name}\".");
                    }

                    var newParameter = (DbParameter) (parameter as ICloneable)?.Clone();

                    if (newParameter == null)
                    {
                        throw new InvalidOperationException($"Cannot clone parameter \"{parameter.ParameterName}\".");
                    }

                    placeholder.Parameter = newParameter;
                }
            }
        }

        protected virtual IReadOnlyList<ParameterPlaceholder> GetParameterPlaceholders(string commandText, IEnumerable<int> indices)
        {
            var placeholders = new List<ParameterPlaceholder>();
            
            foreach (var index in indices)
            {
                var match = Regex.Match(commandText.Substring(index), @"^(?:\?|@\w+)");

                if (!match.Success)
                {
                    throw new InvalidOperationException("Invalid parameter placeholder found.");
                }
                
                placeholders.Add(new ParameterPlaceholder{ Index = index, Name = match.Value });
            }

            return placeholders.AsReadOnly();
        }

        protected virtual IReadOnlyList<int> GetParameterIndices(string sqlFragment)
        {
            var parameterIndices = new List<int>();
            
            // We use '\0' as the default state and char.
            var state = '\0';
            var lastChar = '\0';

            // State machine to count ODBC parameter occurrences.
            for (var i = 0; i < sqlFragment.Length; i++)
            {
                var c = sqlFragment[i];
                
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
                    else if (c == '?' ||
                             c == '@')
                    {
                        parameterIndices.Add(i);
                    }
                }

                if (state == '`' &&
                    c == '`')
                {
                    state = '\0';
                }
            }

            return parameterIndices.AsReadOnly();
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
        
        protected class ParameterPlaceholder
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public DbParameter Parameter { get; set; }
        }
    }
}