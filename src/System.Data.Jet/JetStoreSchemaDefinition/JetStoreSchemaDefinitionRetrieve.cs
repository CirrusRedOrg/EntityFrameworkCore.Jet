using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.OleDb;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace System.Data.Jet.JetStoreSchemaDefinition
{
    /// <summary>
    /// Retrieve metadata from oledb or from system tables
    /// About oledb parameters see here
    /// http://msdn.microsoft.com/en-us/library/cc716722(v=vs.110).aspx
    /// </summary>
    static class JetStoreSchemaDefinitionRetrieve
    {
        static Regex _regExParseShowCommand;

        static SystemTableCollection _systemTables;

        static JetStoreSchemaDefinitionRetrieve()
        {
            _regExParseShowCommand = new Regex(
                @"^\s*show\s*(?<object>\w*)\s*(where\s+(?<condition>.+?))?\s*(order\s+by\s+(?<order>.+))?$",
                RegexOptions.IgnoreCase);

            _systemTables = new SystemTableCollection();
            _systemTables.Refresh();

        }

        public static bool TryGetDataReaderFromShowCommand(DbCommand command, out DbDataReader dataReader)
        {
            if (command.CommandType == System.Data.CommandType.Text && command.CommandText.Trim().StartsWith("show ", StringComparison.InvariantCultureIgnoreCase))
            {
                dataReader = GetDbDataReaderFromSimpleStatement(command.Connection, command.CommandText);
                return true;
            }

            if (command.CommandType == System.Data.CommandType.Text && command.CommandText.IndexOf("show ",0, StringComparison.InvariantCultureIgnoreCase) != 0)
            {
                bool isSchemaTable = false;
                foreach (SystemTable table in _systemTables)
                {
                    isSchemaTable = command.CommandText.IndexOf("show " + table.Name, 0, StringComparison.InvariantCultureIgnoreCase) != -1;
                    if (isSchemaTable)
                        break;
                }
                if (isSchemaTable)
                {
                    dataReader = GetDbDataReaderFromComplexStatement(command.Connection, command);
                    return true;
                }
            }

            dataReader = null;
            return false;
        }

        private static DbDataReader GetDbDataReaderFromComplexStatement(DbConnection connection, DbCommand command)
        {

            string commandText = command.CommandText;

            ConnectionState oldConnectionState = connection.State;

            if (oldConnectionState != ConnectionState.Open)
                connection.Open();

            ClearAllSystemTables(connection);

            List<SystemTable> tablesToCreate = new List<SystemTable>();
            foreach (SystemTable table in _systemTables)
            {
                int showStatementPosition = commandText.IndexOf("show " + table.Name, 0, StringComparison.InvariantCultureIgnoreCase);
                if (showStatementPosition == -1)
                    continue;

                commandText = commandText.ReplaceCaseInsensitive("\\(\\s*show " + table.Name + "\\s*\\)", "[" + table.TableName + "]");
                commandText = commandText.ReplaceCaseInsensitive("show " + table.Name, "[" + table.TableName + "]");
                tablesToCreate.Add(table);
            }

            foreach (SystemTable table in tablesToCreate)
            {
                try
                {
                    DbCommand createTableCommand = connection.CreateCommand();
                    createTableCommand.CommandText = table.CreateStatement;
                    createTableCommand.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // ignored
                }

                DataTable dataTable = table.GetDataTable((OleDbConnection)connection);

                foreach (DataRow row in dataTable.Rows)
                {
                    DbCommand insertCommand = connection.CreateCommand();
                    insertCommand.CommandText = GetInsertStatement(table.TableName, row);
                    insertCommand.ExecuteNonQuery();
                }
            }

            if (
                tablesToCreate.Any(_ => _.Name == "Tables") &&
                tablesToCreate.Any(_ => _.Name == "TableColumns") &&
                tablesToCreate.Any(_ => _.Name == "ViewColumns") &&
                commandText.Contains("'PRIMARY KEY'")
            )
            {
                // Hack
                string whereClause = GetAndFixWhereClause(commandText);
                whereClause = whereClause.Replace("[Extent1]", "[#Tables]");
                commandText = Properties.Resources.StoreSchemaDefinitionRetrieve_QueryHack1 + " " + whereClause;
            }
            else if (
                tablesToCreate.Any(_ => _.Name == "Views") &&
                tablesToCreate.Any(_ => _.Name == "TableColumns") &&
                tablesToCreate.Any(_ => _.Name == "ViewColumns") &&
                commandText.Contains("'PRIMARY KEY'")
            )
            {
                // Hack
                string whereClause = GetAndFixWhereClause(commandText);
                whereClause = whereClause.Replace("[Extent1]", "[#Views]");
                commandText = Properties.Resources.StoreSchemaDefinitionRetrieve_QueryHack2 + " " + whereClause;
            }



            command.CommandText = commandText;
            OleDbDataAdapter dataAdapter = new OleDbDataAdapter((OleDbCommand)command);
            DataSet dataSet = new DataSet();
            dataAdapter.Fill(dataSet);
            DataTable resultDataTable = dataSet.Tables[0];
            DropAllSystemTables(connection);

            if (oldConnectionState != ConnectionState.Open)
                connection.Close();

            return resultDataTable.CreateDataReader();
        }

        private static string GetAndFixWhereClause(string commandText)
        {
            int whereClausePosition = commandText.LastIndexOf("WHERE ", StringComparison.CurrentCultureIgnoreCase);
            string whereClause = commandText.Substring(whereClausePosition);
            return whereClause;
        }

        private static string GetInsertStatement(string tableName, DataRow row)
        {
            string columns = "";
            string values = "";

            foreach (DataColumn column in row.Table.Columns)
            {
                if (row[column] != DBNull.Value)
                {
                    if (!string.IsNullOrWhiteSpace(columns))
                    {
                        columns += ", ";
                        values += ", ";
                    }

                    columns += string.Format("[{0}]", column.ColumnName);
                    dynamic value = row[column];
                    values += JetSyntaxHelper.ToSqlString(value);
                }
            }
            return string.Format("INSERT INTO [{0}] ({1}) VALUES ({2})", tableName, columns, values);
        }

        [DebuggerStepThrough]
        private static void ClearAllSystemTables(DbConnection connection)
        {
            ConnectionState oldConnectionState = connection.State;

            if (oldConnectionState != ConnectionState.Open)
                connection.Open();

            foreach (SystemTable table in _systemTables)
            {
                try
                {
                    var command = connection.CreateCommand();
                    command.CommandText = table.ClearStatement;
                    command.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            if (oldConnectionState != ConnectionState.Open)
                connection.Close();
        }

        [DebuggerStepThrough]
        private static void DropAllSystemTables(DbConnection connection)
        {
            ConnectionState oldConnectionState = connection.State;

            if (oldConnectionState != ConnectionState.Open)
                connection.Open();

            foreach (SystemTable table in _systemTables)
            {
                try
                {
                    var command = connection.CreateCommand();
                    command.CommandText = table.DropStatement;
                    command.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            if (oldConnectionState != ConnectionState.Open)
                connection.Close();
        }


        private static DbDataReader GetDbDataReaderFromSimpleStatement(DbConnection connection, string commandText)
        {
            // Command text format is
            // show <what> [where <condition>] [order by <order>]
            //    <what> can be
            //          tables
            //          tablecolumns
            //
            //          indexes | ix
            //          indexcolumns | ixc
            //
            //          views
            //          viewscolumns
            //          viewconstraints
            //          viewconstraintcolumns
            //          viewforeignkeys
            //
            //          checkconstraints
            //          foreignkeys
            //          foreingkeyconstraints
            //          constraintcolumns
            //
            //          functions
            //          functionparameters
            //          functionreturntablecolumns
            //
            //          procedures
            //          procedureparameters
            //
            //
            //          constraints
            //          constraintcolumns
            //          checkconstraints | cc
            //          foreignkeyconstraints | fkc
            //          foreignkeys | fk
            //          viewconstraints | vc
            //          viewconstraintcolumns | vcc
            //          viewforeignkeys | vfk


            Match match = _regExParseShowCommand.Match(commandText);

            if (!match.Success)
                throw new Exception(string.Format("Unrecognized show statement '{0}'. show syntax is show <object> [where <condition>]", commandText));

            string dbObject = match.Groups["object"].Value;
            string condition = match.Groups["condition"].Value;
            string order = match.Groups["order"].Value;

            DataTable dataTable;

            OleDbConnection oleDbConnection = (OleDbConnection)connection;

            ConnectionState oldConnectionState = connection.State;

            SystemTable systemTable = null;
            try
            {
                systemTable = _systemTables[dbObject];
            }
            catch
            {
                // ignored
            }
            
            if (systemTable == null)
            {
                try
                {
                    if (oldConnectionState != ConnectionState.Open)
                        connection.Open();

                    // Check if is a table not handled by SchemaDefinition
                    switch (dbObject.ToLower())
                    {
                        case "ix":
                        case "indexes":
                            dataTable = GetIndexes(oleDbConnection);
                            break;
                        case "ixc":
                        case "indexcolumns":
                            dataTable = GetIndexColumns(oleDbConnection);
                            break;
                        default:
                            throw new Exception(string.Format("Unknown system table {0}", dbObject));
                    }
                    return dataTable.CreateDataReader();
                }
                finally
                {
                    if (oldConnectionState != ConnectionState.Open)
                        connection.Close();
                }
            }

            if (systemTable == null)
                throw new Exception(string.Format("Unknown system table {0}", dbObject));

            if (oldConnectionState != ConnectionState.Open)
                connection.Open();

            try
            {
                dataTable = systemTable.GetDataTable(oleDbConnection);
            }
            finally
            {
                if (oldConnectionState != ConnectionState.Open)
                    connection.Close();
            }


            DataRow[] selectedRows;
            DataTable selectedDataTable = dataTable.Clone();

            if (!string.IsNullOrWhiteSpace(condition) && !string.IsNullOrWhiteSpace(order))
                selectedRows = dataTable.Select(condition, order);
            else if (!string.IsNullOrWhiteSpace(condition))
                selectedRows = dataTable.Select(condition);
            else if (!string.IsNullOrWhiteSpace(order))
                selectedRows = dataTable.Select("1=1", order);
            else
                return dataTable.CreateDataReader();

            foreach (DataRow row in selectedRows)
                selectedDataTable.ImportRow(row);

            return selectedDataTable.CreateDataReader();
        }


        #region Tables

        public static DataTable GetTables(OleDbConnection connection)
        {
            bool includeSystemTables = false;

            DataTable dataTable = (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_Tables, typeof(DataTable));

            DataTable schemaTable = connection.GetOleDbSchemaTable(
              System.Data.OleDb.OleDbSchemaGuid.Tables,
              new object[] { null, null, null, "TABLE" });

            foreach (System.Data.DataRow table in schemaTable.Rows)
            {
                if (!IsSystemTable(table["TABLE_NAME"].ToString()) || includeSystemTables)
                {
                    dataTable.Rows.Add(
                        table["TABLE_NAME"],
                        "Jet",
                        "Jet",
                        table["TABLE_NAME"],
                        IsSystemTable(table["TABLE_NAME"].ToString()) ? "SYSTEM" : "USER");
                }                
            }

            dataTable.AcceptChanges();

            return dataTable;
        }

        private static bool IsSystemTable(string tableName)
        {
            return
                tableName.StartsWith("msys", StringComparison.InvariantCultureIgnoreCase) ||
                tableName.StartsWith("#", StringComparison.InvariantCultureIgnoreCase);
        }

        public static DataTable GetTableColumns(OleDbConnection connection)
        {
            DataTable dataTable = (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_TableColumns, typeof(DataTable));
            Dictionary<string, string> objectsToGet = GetTablesOrViewDictionary(connection, true);

            GetTableOrViewColumns(connection, dataTable, objectsToGet);

            return dataTable;
        }

        #endregion


        #region Indexes

        public static DataTable GetIndexes(OleDbConnection connection)
        {
            DataTable dataTable = (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_Indexes, typeof(DataTable));

            DataTable schemaTable = connection.GetOleDbSchemaTable(
              System.Data.OleDb.OleDbSchemaGuid.Indexes,
              new object[] { });

            foreach (System.Data.DataRow table in schemaTable.Rows)
                if (
                    Convert.ToInt32(table["ORDINAL_POSITION"]) == 1  // Only the first field of the index
                    )
                    dataTable.Rows.Add(
                        (string)table["TABLE_NAME"] + "." + (string)table["INDEX_NAME"], // Id
                        table["TABLE_NAME"], // ParentId
                        table["TABLE_NAME"], // Table
                        table["INDEX_NAME"], // Name
                        Convert.ToBoolean(table["UNIQUE"]), // IsUnique
                        Convert.ToBoolean(table["PRIMARY_KEY"]) // IsPrimary
                    );

            dataTable.AcceptChanges();

            return dataTable;
        }

        public static DataTable GetIndexColumns(OleDbConnection connection)
        {
            DataTable dataTable = (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_IndexColumns, typeof(DataTable));

            DataTable schemaTable = connection.GetOleDbSchemaTable(
              System.Data.OleDb.OleDbSchemaGuid.Indexes,
              new object[] { });

            foreach (System.Data.DataRow table in schemaTable.Rows)
                dataTable.Rows.Add(
                    (string)table["TABLE_NAME"] + "." + (string)table["INDEX_NAME"] + "." + (string)table["COLUMN_NAME"], // Id
                    table["TABLE_NAME"] + "." + (string)table["INDEX_NAME"], // ParentId
                    table["TABLE_NAME"] + "." + table["COLUMN_NAME"], // ColumnId
                    table["TABLE_NAME"], // Table
                    table["INDEX_NAME"], // Index
                    Convert.ToBoolean(table["UNIQUE"]), // IsUnique
                    Convert.ToBoolean(table["PRIMARY_KEY"]), // IsPrimary
                    table["COLUMN_NAME"], // Name
                    Convert.ToInt32(table["ORDINAL_POSITION"]) // Ordinal
                );

            dataTable.AcceptChanges();

            return dataTable;
        }

        #endregion

        #region Views

        public static DataTable GetViews(OleDbConnection connection)
        {
            DataTable dataTable = (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_Views, typeof(DataTable));

            DataTable schemaTable;

            schemaTable = connection.GetSchema("Views");
            foreach (System.Data.DataRow table in schemaTable.Rows)
                dataTable.Rows.Add(table["TABLE_NAME"], "Jet", "Jet", table["TABLE_NAME"], table["VIEW_DEFINITION"], table["IS_UPDATABLE"]);

            schemaTable = connection.GetOleDbSchemaTable(
                System.Data.OleDb.OleDbSchemaGuid.Procedures,
                null);
            foreach (System.Data.DataRow table in schemaTable.Rows)
                dataTable.Rows.Add(table["PROCEDURE_NAME"], "Jet", "Jet", table["PROCEDURE_NAME"], table["PROCEDURE_DEFINITION"], false);

            dataTable.AcceptChanges();

            return dataTable;
        }

        /// <summary>
        /// Gets the views via OleDb schema table.
        /// No definition can be retrieved
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <returns></returns>
        public static DataTable GetViewsViaGetOleDbSchemaTable(OleDbConnection connection)
        {
            DataTable dataTable = (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_Views, typeof(DataTable));

            DataTable schemaTable = connection.GetOleDbSchemaTable(
              System.Data.OleDb.OleDbSchemaGuid.Tables,
              new object[] { null, null, null, "VIEW" });

            foreach (System.Data.DataRow table in schemaTable.Rows)
                dataTable.Rows.Add(table["TABLE_NAME"], "Jet", "Jet", table["TABLE_NAME"], DBNull.Value, 0);

            dataTable.AcceptChanges();

            return dataTable;
        }

        public static DataTable GetViewColumns(OleDbConnection connection)
        {
            DataTable dataTable = (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_ViewColumns, typeof(DataTable));
            Dictionary<string, string> objectsToGet = GetTablesOrViewDictionary(connection, false);

            GetTableOrViewColumns(connection, dataTable, objectsToGet);

            return dataTable;
        }


        public static DataTable GetViewForeignKeys(OleDbConnection connection)
        {
            return (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_ViewForeignKeys, typeof(DataTable));
        }

        public static DataTable GetViewConstraintColumns(OleDbConnection connection)
        {
            return (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_ViewConstraintColumns, typeof(DataTable));
        }

        public static DataTable GetViewConstraints(OleDbConnection connection)
        {
            return (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_ViewConstraints, typeof(DataTable));
        }


        #endregion

        #region Constraints


        public static DataTable GetConstraints(OleDbConnection connection)
        {
            DataTable dataTable = (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_Constraints, typeof(DataTable));

            DataTable schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Check_Constraints, new object[] { });

            foreach (System.Data.DataRow table in schemaTable.Rows)
                dataTable.Rows.Add(
                    table["CONSTRAINT_NAME"], // Id
                    DBNull.Value, // ParentId
                    table["CONSTRAINT_NAME"], // Name
                    "CHECK", // ConstraintType
                    false,  // IsDeferrable
                    false   // IsIntiallyDeferred
                    );

            schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Foreign_Keys, new object[] { });

            foreach (System.Data.DataRow table in schemaTable.Rows)
                if (Convert.ToInt32(table["ORDINAL"]) == 1)
                    dataTable.Rows.Add(
                        table["FK_NAME"], // Id
                        table["FK_TABLE_NAME"], // ParentId
                        table["FK_NAME"], // Name
                        CONSTRAINTTYPE_FOREIGNKEY, // ConstraintType
                        table["DEFERRABILITY"],  // IsDeferrable
                        false   // IsIntiallyDeferred
                        );

            schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Primary_Keys, new object[] { });

            foreach (System.Data.DataRow table in schemaTable.Rows)
                if (Convert.ToInt32(table["ORDINAL"]) == 1)
                    dataTable.Rows.Add(
                        table["TABLE_NAME"] + "." + table["PK_NAME"], // Id
                        table["TABLE_NAME"], // ParentId
                        table["PK_NAME"], // Name
                        CONSTRAINTTYPE_PRIMARYKEY, // ConstraintType
                        false,  // IsDeferrable
                        false   // IsIntiallyDeferred
                        );

            schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Indexes, new object[] { });

            foreach (System.Data.DataRow table in schemaTable.Rows)
            {
                if (
                    !IsSystemTable(table["TABLE_NAME"].ToString()) &&

                    Convert.ToInt32(table["ORDINAL_POSITION"]) == 1 &&  // Only the first field of the index
                    Convert.ToBoolean(table["PRIMARY_KEY"]) == false && // Not a primary key
                    Convert.ToBoolean(table["UNIQUE"]) == true           // Unique constraint
                )
                    dataTable.Rows.Add(
                        (string)table["TABLE_NAME"] + "." + (string)table["INDEX_NAME"], // Id
                        table["TABLE_NAME"], // ParentId
                        table["INDEX_NAME"], // Name
                        CONSTRAINTTYPE_UNIQUE, // ConstraintType
                        false,  // IsDeferrable
                        false   // IsIntiallyDeferred
                    );
            }

            dataTable.AcceptChanges();

            return dataTable;
        }



        public static DataTable GetConstraintColumns(OleDbConnection connection)
        {
            DataTable dataTable = (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_ConstraintColumns, typeof(DataTable));


            DataTable schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Foreign_Keys, new object[] { });
            foreach (System.Data.DataRow table in schemaTable.Rows)
                dataTable.Rows.Add(
                    table["FK_NAME"], // ConstraintId
                    table["FK_TABLE_NAME"] + "." + table["FK_COLUMN_NAME"], // ColumnId
                    table["FK_NAME"], // ConstraintName
                    CONSTRAINTTYPE_FOREIGNKEY, // ConstraintType
                    table["FK_TABLE_NAME"], // TableName
                    table["FK_COLUMN_NAME"], // ColumnName
                    table["ORDINAL"] //ColumnOrdinal
                    );

            schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Primary_Keys, new object[] { });
            foreach (System.Data.DataRow table in schemaTable.Rows)
                dataTable.Rows.Add(
                    table["TABLE_NAME"] + "." + table["PK_NAME"], // ConstraintId
                    table["TABLE_NAME"] + "." + table["COLUMN_NAME"], // ColumnId
                    table["PK_NAME"], // ConstraintName
                    CONSTRAINTTYPE_PRIMARYKEY, //ConstraintType
                    table["TABLE_NAME"], // TableName
                    table["COLUMN_NAME"], // ColumnName
                    table["ORDINAL"] //ColumnOrdinal
                    );

            schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Indexes, new object[] { });

            foreach (System.Data.DataRow table in schemaTable.Rows)
                if (
                    Convert.ToBoolean(table["PRIMARY_KEY"]) == false && // Not a primary key
                    Convert.ToBoolean(table["UNIQUE"]) == true           // Unique constraint
                    )
                    dataTable.Rows.Add(
                        table["TABLE_NAME"] + "." + table["INDEX_NAME"], // ConstraintId
                        table["TABLE_NAME"] + "." + table["COLUMN_NAME"], // ColumnId
                        table["INDEX_NAME"], // ConstraintName
                        CONSTRAINTTYPE_UNIQUE, // ConstraintType
                        table["TABLE_NAME"], // TableName
                        table["COLUMN_NAME"], // ColumnName
                        table["ORDINAL_POSITION"] //ColumnOrdinal
                        );

            dataTable.AcceptChanges();

            return dataTable;
        }


        #endregion

        #region CheckConstraints

        public static DataTable GetCheckConstraints(OleDbConnection connection)
        {
            DataTable dataTable = (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_CheckConstraints, typeof(DataTable));

            DataTable schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Check_Constraints, new object[] { });

            foreach (System.Data.DataRow table in schemaTable.Rows)
                dataTable.Rows.Add(
                    table["CONSTRAINT_NAME"], // Id
                    table["CHECK_CLAUSE"] // Expression
                    );

            dataTable.AcceptChanges();

            return dataTable;
        }

        #endregion

        #region Foreign Key Constraint


        public static DataTable GetForeignKeyConstraints(OleDbConnection connection)
        {
            DataTable dataTable = (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_ForeignKeyConstraints, typeof(DataTable));

            DataTable schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Foreign_Keys, new object[] { });

            foreach (System.Data.DataRow table in schemaTable.Rows)
                if (Convert.ToInt32(table["ORDINAL"]) == 1)
                    dataTable.Rows.Add(
                        table["FK_NAME"], // Id
                        table["PK_TABLE_NAME"], // ToTableId
                        table["FK_TABLE_NAME"], // FromTableId
                        table["UPDATE_RULE"], // Update rule
                        table["DELETE_RULE"] // Delete rule
                        );


            dataTable.AcceptChanges();

            return dataTable;
        }

        // GetForeignKeyConstraintColumns
        public static DataTable GetForeignKeys(OleDbConnection connection)
        {
            DataTable dataTable = (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_ForeignKeys, typeof(DataTable));

            DataTable schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Foreign_Keys, new object[] { });

            foreach (System.Data.DataRow table in schemaTable.Rows)
                dataTable.Rows.Add(
                    table["FK_NAME"] + "." + table["ORDINAL"], // Id
                    table["ORDINAL"], // Ordinal
                    table["FK_NAME"], // ConstraintId
                    table["FK_TABLE_NAME"] + "." + table["FK_COLUMN_NAME"], // FromColumnId
                    table["PK_TABLE_NAME"] + "." + table["PK_COLUMN_NAME"], // ToColumnId
                    table["FK_TABLE_NAME"], // FromTable
                    table["FK_COLUMN_NAME"], // FromColumn
                    table["PK_TABLE_NAME"], // ToTable
                    table["PK_COLUMN_NAME"], // ToColumn
                    table["UPDATE_RULE"], // Update rule
                    table["DELETE_RULE"] // Delete rule
                    );


            dataTable.AcceptChanges();

            return dataTable;
        }

        #endregion

        #region Procedures

        public static DataTable GetProcedureParameters(OleDbConnection connection)
        {
            return (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_ProcedureParameters, typeof(DataTable));
        }

        public static DataTable GetProcedures(OleDbConnection oleDbConnection)
        {
            return (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_Procedures, typeof(DataTable));
        }

        #endregion

        #region Functions

        public static DataTable GetFunctionReturnTableColumns(OleDbConnection connection)
        {
            return (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_FunctionReturnTableColumns, typeof(DataTable));
        }

        public static DataTable GetFunctionParameters(OleDbConnection connection)
        {
            return (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_FunctionParameters, typeof(DataTable));
        }

        public static DataTable GetFunctions(DbConnection connection)
        {
            return (DataTable)XmlObjectSerializer.GetObject(Properties.Resources.StoreSchemaDefinition_Functions, typeof(DataTable));
        }

        #endregion

        #region General purpose methods

        private static void GetTableOrViewColumns(OleDbConnection connection, DataTable dataTable, Dictionary<string, string> objectsToGet)
        {

            DataTable schemaTable = connection.GetOleDbSchemaTable(
              System.Data.OleDb.OleDbSchemaGuid.Columns,
              new object[] { null, null, null, null });

            foreach (System.Data.DataRow rowColumn in schemaTable.Rows)
            {
                if (objectsToGet.ContainsKey(rowColumn["TABLE_NAME"].ToString()))
                {

                    dataTable.Rows.Add(
                        rowColumn["TABLE_NAME"] + "." + rowColumn["COLUMN_NAME"], // Id
                        rowColumn["TABLE_NAME"], // ParentId
                        rowColumn["TABLE_NAME"], // Table
                        rowColumn["COLUMN_NAME"], // Name
                        rowColumn["ORDINAL_POSITION"],  // Ordinal
                        GetIsNullable(connection, rowColumn) ? 1 : 0, // It seems that sometimes IS_NULLABLE from OleDb is wrong - Convert.ToBoolean(rowColumn["IS_NULLABLE"]) ? 1 : 0, // IsNullable
                        ConvertToJetDataType(Convert.ToInt32(rowColumn["DATA_TYPE"]), Convert.ToInt32(rowColumn["COLUMN_FLAGS"])), // TypeName
                        rowColumn["CHARACTER_MAXIMUM_LENGTH"], // Max length
                        rowColumn["NUMERIC_PRECISION"], // Precision
                        rowColumn["DATETIME_PRECISION"], //DateTimePrecision
                        rowColumn["NUMERIC_SCALE"], // Scale

                        rowColumn["COLLATION_CATALOG"],	//CollationCatalog
                        rowColumn["COLLATION_SCHEMA"],	//CollationSchema
                        rowColumn["COLLATION_NAME"], //CollationName
                        rowColumn["CHARACTER_SET_CATALOG"],	//CharacterSetCatalog
                        rowColumn["CHARACTER_SET_SCHEMA"], //CharacterSetSchema
                        rowColumn["CHARACTER_SET_NAME"], //CharacterSetName
                        0,      //IsMultiSet
                        GetIsIdentity(connection, rowColumn), // IsIdentity
                        Convert.ToBoolean(rowColumn["COLUMN_HASDEFAULT"]) ? 1 : 0, // IsStoreGenerated
                        rowColumn["COLUMN_DEFAULT"], // Default
                        GetIsKey(connection, rowColumn) // IsKey
                    );
                }
            }
            dataTable.AcceptChanges();
        }


        private static Dictionary<string, string> GetTablesOrViewDictionary(OleDbConnection connection, bool getTables)
        {
            DataTable schemaTable = connection.GetOleDbSchemaTable(
              System.Data.OleDb.OleDbSchemaGuid.Tables,
              new object[] { null, null, null, getTables ? "TABLE" : "VIEW" });

            Dictionary<string, string> list = new Dictionary<string, string>(schemaTable.Rows.Count, StringComparer.InvariantCultureIgnoreCase);

            foreach (System.Data.DataRow table in schemaTable.Rows)
            {
                if (!IsSystemTable(table["TABLE_NAME"].ToString()))
                    list.Add(table["TABLE_NAME"].ToString(), table["TABLE_NAME"].ToString());
            }
            return list;
        }


        private static string ConvertToJetDataType(int intOleDbType, int intFlags)
        {

            OleDbColumnFlag flags = (OleDbColumnFlag)intFlags;

            switch (((OleDbType)intOleDbType))
            {
                case OleDbType.BigInt:
                    return "int";       // In Jet this is 32 bit while bigint is 64 bits
                case OleDbType.Binary:
                    if (flags.HasFlag(OleDbColumnFlag.IsLong))
                        return "image";
                    else if (flags.HasFlag(OleDbColumnFlag.IsFixedLength))
                        return "binary";
                    else
                        return "varbinary";
                case OleDbType.Boolean:
                    return "bit";
                case OleDbType.Char:
                    return "char";
                case OleDbType.Currency:
                    return "decimal";
                case OleDbType.DBDate:
                case OleDbType.Date:
                case OleDbType.DBTimeStamp:
                case OleDbType.DBTime:
                    return "datetime";
                case OleDbType.Decimal:
                case OleDbType.Numeric:
                    return "decimal";
                case OleDbType.Double:
                    return "double";
                case OleDbType.Integer:
                    return "int";
                case OleDbType.Single:
                    return "single";
                case OleDbType.SmallInt:
                    return "smallint";
                case OleDbType.TinyInt:
                    return "smallint";  // Signed byte not handled by jet so we need 16 bits
                case OleDbType.UnsignedTinyInt:
                    return "byte";
                case OleDbType.LongVarBinary:
                case OleDbType.VarBinary:
                    return "varbinary";
                case OleDbType.VarChar:
                case OleDbType.LongVarChar:
                    return "varchar";
                case OleDbType.WChar:
                    if (flags.HasFlag(OleDbColumnFlag.IsLong))
                        return "text";
                    else if (flags.HasFlag(OleDbColumnFlag.IsFixedLength))
                        return "char";
                    else
                        return "varchar";
                case OleDbType.Guid:
                    return "guid";
                case OleDbType.BSTR:
                case OleDbType.Variant:
                case OleDbType.VarWChar:
                case OleDbType.VarNumeric:
                case OleDbType.Error:
                case OleDbType.Empty:
                case OleDbType.Filetime:
                case OleDbType.IDispatch:
                case OleDbType.IUnknown:
                case OleDbType.UnsignedBigInt:
                case OleDbType.UnsignedInt:
                case OleDbType.UnsignedSmallInt:
                case OleDbType.PropVariant:
                default:
                    throw new ArgumentException(string.Format("The data type {0} is not handled by Jet. Did you retrieve this from Jet?", ((OleDbType)intOleDbType)));
            }
        }


        static string _lastTableName;
        static DataTable _lastStructureDataTable;
        static object _lastStructureDataTableLock = new object();

        private const string CONSTRAINTTYPE_FOREIGNKEY = "FOREIGN KEY";
        private const string CONSTRAINTTYPE_PRIMARYKEY = "PRIMARY KEY";
        private const string CONSTRAINTTYPE_UNIQUE = "UNIQUE";

        private static bool GetIsIdentity(IDbConnection connection, DataRow rowColumn)
        {
            if (Convert.ToInt32(rowColumn["COLUMN_FLAGS"]) != 0x5a || Convert.ToInt32(rowColumn["DATA_TYPE"]) != 3)
                return false;

            DataRow fieldRow = GetFieldRow(connection, (string)rowColumn["TABLE_NAME"], (string)rowColumn["COLUMN_NAME"]);

            if (fieldRow == null)
                return false;

            return (bool)fieldRow["IsAutoIncrement"];

            /*
             * This are all the types we can use
                        c.Name = (string)fieldRow["ColumnName"];
                        c.Length = (int)fieldRow["ColumnSize"];
                        c.NumericPrecision = DBToShort(fieldRow["NumericPrecision"]);
                        c.NumericScale = DBToShort(fieldRow["NumericScale"]);
                        c.NetType = (System.Type)fieldRow["DataType"];
                        c.ProviderType = fieldRow["ProviderType"];
                        if (connection.DBType == DBType.SQLite)
                            c.IsLong = c.Length > 65535;
                        else
                            c.IsLong = (bool)fieldRow["IsLong"];
                        c.AllowDBNull = (bool)fieldRow["AllowDBNull"];
                        c.IsUnique = (bool)fieldRow["IsUnique"];

                        c.IsKey = (bool)fieldRow["IsKey"];

                        c.SchemaName = DBToString(fieldRow["BaseSchemaName"]);

                            c.CatalogName = DBToString(fieldRow["BaseCatalogName"]);

                        c.ComputeSQLDataType();

             */
        }

        private static bool GetIsNullable(IDbConnection connection, DataRow rowColumn)
        {
            DataRow fieldRow = GetFieldRow(connection, (string)rowColumn["TABLE_NAME"], (string)rowColumn["COLUMN_NAME"]);

            if (fieldRow == null)
                return Convert.ToBoolean(rowColumn["IS_NULLABLE"]);

            return (bool) fieldRow["AllowDBNull"] && Convert.ToBoolean(rowColumn["IS_NULLABLE"]);
        }

        private static bool GetIsKey(IDbConnection connection, DataRow rowColumn)
        {
            if (Convert.ToInt32(rowColumn["COLUMN_FLAGS"]) != 0x5a || Convert.ToInt32(rowColumn["DATA_TYPE"]) != 3)
                return false;

            DataRow fieldRow = GetFieldRow(connection, (string)rowColumn["TABLE_NAME"], (string)rowColumn["COLUMN_NAME"]);

            if (fieldRow == null)
                return false;

            return (bool)fieldRow["IsKey"];

            /*
             * This are all the types we can use
                        c.Name = (string)fieldRow["ColumnName"];
                        c.Length = (int)fieldRow["ColumnSize"];
                        c.NumericPrecision = DBToShort(fieldRow["NumericPrecision"]);
                        c.NumericScale = DBToShort(fieldRow["NumericScale"]);
                        c.NetType = (System.Type)fieldRow["DataType"];
                        c.ProviderType = fieldRow["ProviderType"];
                        if (connection.DBType == DBType.SQLite)
                            c.IsLong = c.Length > 65535;
                        else
                            c.IsLong = (bool)fieldRow["IsLong"];
                        c.AllowDBNull = (bool)fieldRow["AllowDBNull"];
                        c.IsUnique = (bool)fieldRow["IsUnique"];

                        c.IsKey = (bool)fieldRow["IsKey"];

                        c.SchemaName = DBToString(fieldRow["BaseSchemaName"]);

                            c.CatalogName = DBToString(fieldRow["BaseCatalogName"]);

                        c.ComputeSQLDataType();

             */
        }

        private static DataRow GetFieldRow(IDbConnection connection, string tableName, string columnName)
        {
            lock(_lastStructureDataTableLock)
            {
                if (_lastTableName != tableName)
                {
                    ReadLastStructureDataTable(connection, tableName);
                }

                DataRow[] fieldRows = _lastStructureDataTable.Select(string.Format("ColumnName = '{0}'", columnName.Replace("'", "''")));

                if (fieldRows.Length == 0) 
                {
                    // Structure changed since last refresh?
                    ReadLastStructureDataTable(connection, tableName);
                    fieldRows = _lastStructureDataTable.Select(string.Format("ColumnName = '{0}'", columnName.Replace("'", "''")));
                    if (fieldRows.Length == 0)  // Second error
                    {
                        return null;
                    }
                }

                return fieldRows[0];
            }
        }

        private static void ReadLastStructureDataTable(IDbConnection connection, string tableName)
        {
            _lastTableName = tableName;

            // This is the standard read column for DBMS
            string sql = string.Empty;

            sql += "Select ";
            sql += "    * ";
            sql += "From ";
            sql += string.Format("    {0} ", JetSyntaxHelper.QuoteIdentifier(_lastTableName));
            sql += "Where ";
            sql += "    1 = 2 ";

            IDbCommand command = null;
            IDataReader dataReader = null;

            try
            {
                command = connection.CreateCommand();
                command.CommandText = sql;

                dataReader = command.ExecuteReader(CommandBehavior.KeyInfo);

                _lastStructureDataTable = dataReader.GetSchemaTable();
            }

            finally
            {
                // Exceptions will not be catched but these instructions will be executed anyway
                if (command != null)
                    command.Dispose();

                if (dataReader != null)
                    dataReader.Dispose();
            }
        }

        #endregion



    }
}
