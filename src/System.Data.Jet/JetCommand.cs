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

        private static readonly Regex _skipRegularExpression = new Regex(@"\bskip\s(?<stringSkipCount>@.*)\b", RegexOptions.IgnoreCase);


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
            LogHelper.ShowCommandText("ExecuteDbDataReader", _WrappedCommand);

            DbDataReader dataReader;
            if (JetStoreSchemaDefinitionRetrieve.TryGetDataReaderFromShowCommand(_WrappedCommand, out dataReader))
            {
                // Retrieve of store schema definition
                return dataReader;
            }

            if (_WrappedCommand.CommandType != CommandType.Text)
                return new JetDataReader(_WrappedCommand.ExecuteReader(behavior));

            string[] commandTextList = SplitCommands(_WrappedCommand.CommandText);

            JetDataReader jetDataReader = null;
            for (int i = 0; i < commandTextList.Length; i++)
            {
                string commandText = commandTextList[i];
                commandText = ParseIdentity(commandText);
                commandText = ParseGuid(commandText);

                jetDataReader = InternalExecuteDbDataReader(commandText, behavior);
            }

            return jetDataReader;
        }


        /// <summary>
        /// Executes the non query.
        /// </summary>
        /// <returns></returns>
        public override int ExecuteNonQuery()
        {
            LogHelper.ShowCommandText("ExecuteNonQuery", _WrappedCommand);

            if (JetStoreDatabaseHandling.TryDatabaseOperation(_WrappedCommand.CommandText))
                return 1;

            if (_WrappedCommand.CommandType != CommandType.Text)
                return _WrappedCommand.ExecuteNonQuery();

            string[] commandTextList = SplitCommands(_WrappedCommand.CommandText);

            int returnValue = -1;
            for (int i = 0; i < commandTextList.Length; i++)
            {
                string commandText = commandTextList[i];
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
            SortParameters(_WrappedCommand.Parameters);
            FixParameters(_WrappedCommand.Parameters);

            DbCommand command;
            command = (DbCommand)((ICloneable)this._WrappedCommand).Clone();
            command.CommandText = newCommandText;

            if (skipCount != 0)
                return new JetDataReader(command.ExecuteReader(behavior), topCount == -1 ? 0 : topCount - skipCount, skipCount);
            else if (topCount >= 0)
                return new JetDataReader(command.ExecuteReader(behavior), topCount, 0);
            else
                return new JetDataReader(command.ExecuteReader(behavior));
        }

        private int InternalExecuteNonQuery(string commandText)
        {

            int topCount;
            int skipCount;
            string newCommandText;
            ParseSkipTop(commandText, out topCount, out skipCount, out newCommandText);
            //ApplyParameters(newCommandText, _WrappedCommand.Parameters, out newCommandText);
            SortParameters(_WrappedCommand.Parameters);
            FixParameters(_WrappedCommand.Parameters);

            DbCommand command;
            command = (DbCommand)((ICloneable)this._WrappedCommand).Clone();
            command.CommandText = newCommandText;

            return command.ExecuteNonQuery();

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

        private void SortParameters(DbParameterCollection parameters)
        {
            if (parameters.Count == 0)
                return;
            var parameterArray = parameters.Cast<OleDbParameter>().ToArray();
            Array.Sort(parameterArray, ParameterComparer.Instance);

            parameters.Clear();
            foreach (OleDbParameter parameter in parameterArray)
                parameters.Add(new OleDbParameter(parameter.ParameterName, parameter.Value));
        }

        private class ParameterComparer : IComparer<DbParameter>
        {
            public static readonly ParameterComparer Instance = new ParameterComparer();

            private ParameterComparer() { }

            Regex _extractNumberRegex = new Regex(@"^@p(?<number>\d+)$", RegexOptions.IgnoreCase);
            public int Compare(DbParameter x, DbParameter y)
            {
                Match xMatch = _extractNumberRegex.Match(x.ParameterName);
                if (!xMatch.Success)
                    return -1;
                Match yMatch = _extractNumberRegex.Match(y.ParameterName);
                if (!yMatch.Success)
                    return -1;

                var xNumber = int.Parse(xMatch.Groups["number"].Value);
                var yNumber = int.Parse(yMatch.Groups["number"].Value);
                return xNumber.CompareTo(yNumber);
            }
        }


        private string[] SplitCommands(string command)
        {
            string[] commandParts =
                command.Replace("\r\n", "\n").Replace("\r", "\n")
                    .Split(new[] { ";\n" }, StringSplitOptions.None);
            List<string> commands = new List<string>(commandParts.Length);
            foreach (string commandPart in commandParts)
            {
                if (!string.IsNullOrWhiteSpace(commandPart.Replace("\n", "").Replace(";", "")))
                    commands.Add(commandPart);
            }
            return commands.ToArray();
        }


        private string ParseIdentity(string commandText)
        {
            if (commandText.ToLower().Contains("@@identity"))
            {
                DbCommand command;
                command = (DbCommand)((ICloneable)this._WrappedCommand).Clone();
                command.CommandText = "Select @@identity";
                object identity = command.ExecuteScalar();
                int iIdentity = Convert.ToInt32(identity);
                Console.WriteLine("@@identity = {0}", iIdentity);
                return Regex.Replace(commandText, "@@identity", iIdentity.ToString(System.Globalization.CultureInfo.InvariantCulture), RegexOptions.IgnoreCase);
            }
            return commandText;
        }


        private Guid? _lastGuid = null;

        private string ParseGuid(string commandText)
        {
            while (commandText.ToLower().Contains("newguid()"))
            {
                _lastGuid = Guid.NewGuid();
                commandText = Regex.Replace(commandText, @"newguid\(\)", string.Format("{{{0}}}", _lastGuid), RegexOptions.IgnoreCase);
            }

            if (commandText.ToLower().Contains("@@guid"))
            {
                Console.WriteLine("@@guid = {{{0}}}", _lastGuid);
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
                string stringTopCount = newCommandText.Substring(indexOfTop + 5, indexOfTopEnd - indexOfTop - 5).Trim();
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

                int localTopCount = topCount0 + topCount1;
                newCommandText = newCommandText.Remove(indexOfTop + 5, stringTopCount.Length).Insert(indexOfTop + 5, localTopCount.ToString());
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
                stringSkipCount = matchSkipRegularExpression.Groups["stringSkipCount"].Value;

                if (stringSkipCount.StartsWith("@"))
                    skipCount = Convert.ToInt32(_WrappedCommand.Parameters[stringSkipCount].Value);
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
