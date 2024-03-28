using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using EntityFrameworkCore.Jet.Data.JetStoreSchemaDefinition;
using System.Diagnostics;

namespace EntityFrameworkCore.Jet.Data
{
    public class AdoxSchema : SchemaProvider
    {
        private readonly bool _naturalOnly;
        private readonly dynamic _connection;
        private readonly dynamic _catalog;

        private bool _ignoreMsys;
        public AdoxSchema(JetConnection connection, bool naturalOnly, bool readOnly)
            : this(connection, readOnly)
        {
            _naturalOnly = naturalOnly;
        }

        public AdoxSchema(JetConnection connection, bool readOnly)
        {
            _connection = new ComObject("ADODB.Connection");
            _ignoreMsys = connection.IgnoreMsys;
            try
            {
                var connectionString = GetOleDbConnectionString(connection.ActiveConnectionString);
                _connection.Open(connectionString);

                _catalog = new ComObject("ADOX.Catalog");

                try
                {
                    _catalog.ActiveConnection = _connection;
                }
                catch
                {
                    _catalog.Dispose();
                    throw;
                }
            }
            catch
            {
                _connection.Dispose();
                throw;
            }
        }

        private static string GetOleDbConnectionString(string fileNameOrConnectionString)
        {
            if (JetStoreDatabaseHandling.IsConnectionString(fileNameOrConnectionString) &&
                JetConnection.GetDataAccessProviderType(fileNameOrConnectionString) == DataAccessProviderType.OleDb)
            {
                return fileNameOrConnectionString;
            }

            var filePath = JetStoreDatabaseHandling.ExpandFileName(JetStoreDatabaseHandling.ExtractFileNameFromConnectionString(fileNameOrConnectionString));
            var connectionString = JetConnection.GetConnectionString(filePath, DataAccessProviderType.OleDb);

            if (JetStoreDatabaseHandling.IsConnectionString(fileNameOrConnectionString) &&
                JetConnection.GetDataAccessProviderType(fileNameOrConnectionString) == DataAccessProviderType.Odbc)
            {
                var oldCsb = new DbConnectionStringBuilder(true) { ConnectionString = fileNameOrConnectionString };
                var newCsb = new DbConnectionStringBuilder { ConnectionString = connectionString };

                newCsb.SetUserId(oldCsb.GetUserId(DataAccessProviderType.Odbc), DataAccessProviderType.OleDb);
                newCsb.SetPassword(oldCsb.GetPassword(DataAccessProviderType.Odbc), DataAccessProviderType.OleDb);
                newCsb.SetSystemDatabase(oldCsb.GetSystemDatabase(DataAccessProviderType.Odbc), DataAccessProviderType.OleDb);
                newCsb.SetDatabasePassword(oldCsb.GetDatabasePassword(DataAccessProviderType.Odbc), DataAccessProviderType.OleDb);

                connectionString = newCsb.ConnectionString;
            }

            return connectionString;
        }

        public override DataTable GetTables()
        {
            var dataTable = SchemaTables.GetTablesDataTable();

            // Only necessary, if ADOX is used in conjunction with ODBC.
            // var procedureSet = new HashSet<string>();
            // using var procedures = _catalog.Procedures;
            // if (procedures != null)
            // {
            //     var procedureCount = procedures.Count;
            //
            //     for (var i = 0; i < procedureCount; i++)
            //     {
            //         using var procedure = procedures[i];
            //
            //         var procedureName = (string) procedure.Name;
            //         procedureSet.Add(procedureName);
            //     }
            // }

            using var tables = _catalog.Tables;
            var tableCount = tables.Count;

            for (var i = 0; i < tableCount; i++)
            {
                using var table = tables[i];
                using var properties = table.properties;

                var tableName = (string)table.Name;

                if (tableName.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) && _ignoreMsys)
                {
                    continue;
                }

                // Depending on the provider (ODBC or OLE DB) used, the Tables collection might contain VIEWs
                // that take parameters, which makes them procedures.
                // We make sure here, that we exclude any procedures from the returned table list.
                // if (!procedureSet.Contains(tableName))
                {
                    var tableType = (string)table.Type;

                    Debug.Assert(
                        tableType == "TABLE" ||
                        tableType == "LINK" ||
                        tableType == "SYSTEM TABLE" ||
                        tableType == "ACCESS TABLE" ||
                        tableType == "VIEW" ||
                        tableType == "TEMPORARY TABLE");

                    if (IsInternalTableByName(tableName))
                    {
                        tableType = "INTERNAL TABLE";
                    }
                    else if (IsSystemTableByName(tableName))
                    {
                        tableType = "SYSTEM TABLE";
                    }
                    else if (tableType == "TABLE" ||
                             tableType == "LINK")
                    {
                        tableType = "BASE TABLE";
                    }

                    var validationRule = GetPropertyValue<string>(properties, "Jet OLEDB:Table Validation Rule");
                    validationRule = string.IsNullOrEmpty(validationRule)
                        ? null
                        : validationRule;

                    var validationText = GetPropertyValue<string>(properties, "Jet OLEDB:Table Validation Text");
                    validationText = string.IsNullOrEmpty(validationText)
                        ? null
                        : validationText;

                    dataTable.Rows.Add(
                        tableName,
                        tableType,
                        validationRule,
                        validationText);
                }
            }

            dataTable.AcceptChanges();
            return dataTable;
        }

        public override DataTable GetColumns()
        {
            var dataTable = SchemaTables.GetColumnsDataTable();

            // There is no way to get the ordinal position of a column with ADOX. Looks like someone at Microsoft just forgot to
            // implement it.
            // Therefore, either DAO has to be used, or ADOX together with the OpenSchema()
            // method.

            Dictionary<(string TableName, string ColumnName), int> ordinalPositions = null;

            if (!_naturalOnly)
            {
                using (var recordset = _connection.OpenSchema(SchemaEnum.adSchemaColumns))
                {
                    ordinalPositions = new Dictionary<(string TableName, string ColumnName), int>();

                    using var fields = recordset.Fields;
                    using var tableNameField = fields["TABLE_NAME"];
                    using var columnNameField = fields["COLUMN_NAME"];
                    using var ordinalPositionField = fields["ORDINAL_POSITION"];

                    recordset.MoveFirst();

                    while (!recordset.EOF)
                    {
                        var tableName = (string)tableNameField.Value;
                        var columnName = (string)columnNameField.Value;
                        var ordinalPosition = (int)ordinalPositionField.Value - 1;

                        ordinalPositions.Add((tableName, columnName), ordinalPosition);
                        recordset.MoveNext();
                    }

                    recordset.Close();
                }
            }

            using var tables = _catalog.Tables;
            var tableCount = tables.Count;

            for (var i = 0; i < tableCount; i++)
            {
                using var table = tables[i];
                var tableName = (string)table.Name;

                if (tableName.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) && _ignoreMsys)
                {
                    continue;
                }

                using var columns = table.Columns;
                var columnCount = columns.Count;

                for (var j = 0; j < columnCount; j++)
                {
                    using var column = columns[j];
                    using var properties = column.Properties;

                    var columnName = (string)column.Name;
                    var attributes = (ColumnAttributesEnum)column.Attributes;
                    var ordinalPosition = ordinalPositions?[(tableName, columnName)] ?? j;
                    var dataType = (DataTypeEnum)column.Type;

                    // This in inpercise in ADOX, especially for Views. The "Nullable" property is even worse.
                    var nullable = (attributes & ColumnAttributesEnum.adColNullable) == ColumnAttributesEnum.adColNullable;

                    var isIdentity = dataType == DataTypeEnum.adInteger &&
                                     GetPropertyValue<bool>(properties, "Autoincrement");
                    var seed = isIdentity
                        ? (int?)GetPropertyValue<int>(properties, "Seed")
                        : null;
                    var increment = isIdentity
                        ? (int?)GetPropertyValue<int>(properties, "Increment")
                        : null;
                    var dataTypeString = GetDataTypeString(dataType, isIdentity);
                    var numericPrecision = dataType == DataTypeEnum.adDecimal || dataType == DataTypeEnum.adNumeric
                        ? (int?)column.Precision
                        : null;
                    var numericScale = dataType == DataTypeEnum.adDecimal || dataType == DataTypeEnum.adNumeric
                        ? (int?)(byte)column.NumericScale
                        : null;

                    var size = (int)column.DefinedSize;
                    var length = GetMaxLength(dataType, size);

                    var defaultValue = GetPropertyValue<string>(properties, "Default");

                    var validationRule = GetPropertyValue<string>(properties, "Jet OLEDB:Column Validation Rule");
                    validationRule = string.IsNullOrEmpty(validationRule)
                        ? null
                        : validationRule;

                    var validationText = GetPropertyValue<string>(properties, "Jet OLEDB:Column Validation Text");
                    validationText = string.IsNullOrEmpty(validationText)
                        ? null
                        : validationText;

                    dataTable.Rows.Add(
                        tableName,
                        columnName,
                        ordinalPosition,
                        dataTypeString,
                        nullable,
                        length,
                        numericPrecision,
                        numericScale,
                        defaultValue,
                        validationRule,
                        validationText,
                        seed,
                        increment);
                }
            }

            dataTable.AcceptChanges();
            return dataTable;
        }

        public override DataTable GetIndexes()
        {
            var dataTable = SchemaTables.GetIndexesDataTable();

            using var tables = _catalog.Tables;
            var tableCount = tables.Count;

            for (var i = 0; i < tableCount; i++)
            {
                using var table = tables[i];
                var tableName = (string)table.Name;

                if (tableName.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) && _ignoreMsys)
                {
                    continue;
                }

                using var indexes = table.Indexes;
                var indexCount = (int)indexes.Count;

                for (var j = 0; j < indexCount; j++)
                {
                    using var index = indexes[j];

                    var indexName = (string)index.Name;
                    var indexNulls = (AllowNullsEnum)index.IndexNulls;
                    var isPrimaryKey = (bool)index.PrimaryKey;
                    var isUnique = (bool)index.Unique;
                    var isNullable = indexNulls != AllowNullsEnum.adIndexNullsDisallow;
                    var ignoreNulls = isNullable && indexNulls != AllowNullsEnum.adIndexNullsAllow;

                    using var properties = index.Properties;

                    string indexType;
                    if (isPrimaryKey)
                    {
                        indexType = "PRIMARY";
                    }
                    else if (isUnique)
                    {
                        indexType = "UNIQUE";
                    }
                    else
                    {
                        indexType = "INDEX";
                    }

                    dataTable.Rows.Add(
                        tableName,
                        indexName,
                        indexType,
                        isNullable,
                        ignoreNulls);
                }
            }

            dataTable.AcceptChanges();
            return dataTable;
        }

        public override DataTable GetIndexColumns()
        {
            var dataTable = SchemaTables.GetIndexColumnsDataTable();

            using var tables = _catalog.Tables;
            var tableCount = tables.Count;

            for (var i = 0; i < tableCount; i++)
            {
                using var table = tables[i];
                var tableName = (string)table.Name;

                if (tableName.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) && _ignoreMsys)
                {
                    continue;
                }

                using var indexes = table.Indexes;
                var indexCount = (int)indexes.Count;

                for (var j = 0; j < indexCount; j++)
                {
                    using var index = indexes[j];

                    var indexName = (string)index.Name;

                    using var columns = index.Columns;
                    var columnCount = (int)columns.Count;

                    for (var k = 0; k < columnCount; k++)
                    {
                        using var column = columns[k];

                        var fieldName = (string)column.Name;
                        var ordinalPosition = k;
                        var sortOrder = (SortOrderEnum)column.SortOrder;
                        var isDescending = sortOrder == SortOrderEnum.adSortDescending;

                        dataTable.Rows.Add(
                            tableName,
                            indexName,
                            ordinalPosition,
                            fieldName,
                            isDescending);
                    }
                }
            }

            dataTable.AcceptChanges();
            return dataTable;
        }

        public override DataTable GetRelations()
        {
            var dataTable = SchemaTables.GetRelationsDataTable();

            using var tables = _catalog.Tables;
            var tableCount = tables.Count;

            for (var i = 0; i < tableCount; i++)
            {
                using var table = tables[i];
                var referencingTableName = (string)table.Name;

                if (table.Name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) && _ignoreMsys)
                {
                    continue;
                }

                using var keys = table.Keys;
                var keyCount = (int)keys.Count;

                for (var j = 0; j < keyCount; j++)
                {
                    using var key = keys[j];

                    var relationName = (string)key.Name;
                    var principalTableName = (string)key.RelatedTable;

                    var relationType = !_naturalOnly ? "MANY" : null; // we don't know what kind of relationship this is

                    var updateRule = (RuleEnum)key.UpdateRule;
                    var onUpdate = updateRule switch
                    {
                        RuleEnum.adRINone => "NO ACTION",
                        RuleEnum.adRICascade => "CASCADE",
                        RuleEnum.adRISetNull => "SET NULL",
                        RuleEnum.adRISetDefault => "SET DEFAULT",
                        _ => "NO ACTION",
                    };

                    var deleteRule = (RuleEnum)key.DeleteRule;
                    var onDelete = deleteRule switch
                    {
                        RuleEnum.adRINone => "NO ACTION",
                        RuleEnum.adRICascade => "CASCADE",
                        RuleEnum.adRISetNull => "SET NULL",
                        RuleEnum.adRISetDefault => "SET DEFAULT",
                        _ => "NO ACTION",
                    };

                    var isEnforced = _naturalOnly ? null : (bool?)true;
                    var isInherited = _naturalOnly ? null : (bool?)true;

                    dataTable.Rows.Add(
                        relationName,
                        referencingTableName,
                        principalTableName,
                        relationType,
                        onDelete,
                        onUpdate,
                        isEnforced,
                        isInherited);
                }
            }

            dataTable.AcceptChanges();
            return dataTable;
        }

        public override DataTable GetRelationColumns()
        {
            var dataTable = SchemaTables.GetRelationColumnsDataTable();

            using var tables = _catalog.Tables;
            var tableCount = tables.Count;

            for (var i = 0; i < tableCount; i++)
            {
                using var table = tables[i];

                if (table.Name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) && _ignoreMsys)
                {
                    continue;
                }

                using var keys = table.Keys;
                var keyCount = (int)keys.Count;

                for (var j = 0; j < keyCount; j++)
                {
                    using var key = keys[j];

                    var relationName = (string)key.Name;
                    var relationType = (KeyTypeEnum)key.Type;

                    if (relationType == KeyTypeEnum.adKeyForeign)
                    {
                        using var columns = key.Columns;
                        var columnCount = (int)columns.Count;

                        for (var k = 0; k < columnCount; k++)
                        {
                            using var column = columns[k];

                            var referencingColumnName = (string)column.Name;
                            var principalColumnName = (string)column.RelatedColumn;

                            dataTable.Rows.Add(
                                relationName,
                                referencingColumnName,
                                principalColumnName,
                                k + 1);
                        }
                    }
                }
            }

            dataTable.AcceptChanges();
            return dataTable;
        }

        public override DataTable GetCheckConstraints()
        {
            var dataTable = SchemaTables.GetCheckConstraintsDataTable();

            var checkConstraints = new Dictionary<string, string>();

            using (var recordset = _connection.OpenSchema(SchemaEnum.adSchemaCheckConstraints))
            {
                using var fields = recordset.Fields;
                using var constraintNameField = fields["CONSTRAINT_NAME"];
                using var checkClauseField = fields["CHECK_CLAUSE"];
                using var descriptionField = fields["DESCRIPTION"];

                recordset.MoveFirst();

                while (!recordset.EOF)
                {
                    var constraintName = (string)constraintNameField.Value;
                    var checkClause = (string)checkClauseField.Value;

                    checkConstraints.Add(constraintName, checkClause);
                    recordset.MoveNext();
                }
            }

            using (var recordset = _connection.OpenSchema(SchemaEnum.adSchemaTableConstraints))
            {
                using var fields = recordset.Fields;
                using var tableNameField = fields["TABLE_NAME"];
                using var constraintNameField = fields["CONSTRAINT_NAME"];
                using var constraintTypeField = fields["CONSTRAINT_TYPE"];

                recordset.MoveFirst();

                while (!recordset.EOF)
                {
                    var tableName = (string)tableNameField.Value;
                    var constraintName = (string)constraintNameField.Value;
                    //var constraintType = (string) constraintTypeField.Value;
                    //if (constraintType.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase) &&

                    if (checkConstraints.TryGetValue(constraintName, out var checkClause))
                    {
                        dataTable.Rows.Add(
                            tableName,
                            constraintName,
                            checkClause);
                    }

                    recordset.MoveNext();
                }
            }

            dataTable.AcceptChanges();
            return dataTable;
        }

        public override void EnsureDualTable()
        {
            using var tables = _catalog.Tables;

            try
            {
                using var table = tables[JetConnection.DefaultDualTableName];
            }
            catch
            {
                _connection.Execute($"CREATE TABLE `{JetConnection.DefaultDualTableName}` (`ID` INTEGER NOT NULL CONSTRAINT `PrimaryKey` PRIMARY KEY)", out int _, (int)CommandTypeEnum.adCmdText | (int)ExecuteOptionEnum.adExecuteNoRecords);
                _connection.Execute($"INSERT INTO `{JetConnection.DefaultDualTableName}` (`ID`) VALUES (1)", out int _, (int)CommandTypeEnum.adCmdText | (int)ExecuteOptionEnum.adExecuteNoRecords);
                _connection.Execute($"ALTER TABLE `{JetConnection.DefaultDualTableName}` ADD CONSTRAINT `SingleRecord` CHECK ((SELECT COUNT(*) FROM [{JetConnection.DefaultDualTableName}]) = 1)", out int _, (int)CommandTypeEnum.adCmdText | (int)ExecuteOptionEnum.adExecuteNoRecords);

                try
                {
                    tables.Refresh();
                    using var table = tables[JetConnection.DefaultDualTableName];
                    using var properties = table.Properties;
                    using var property = properties["Jet OLEDB:Table Hidden In Access"];
                    property.Value = true;
                }
                catch (Exception e)
                {
                    throw new Exception($"Cannot create dual table '{JetConnection.DefaultDualTableName}' using ADOX.", e);
                }
            }
        }

        public override void RenameTable(string oldTableName, string newTableName)
        {
            if (string.IsNullOrWhiteSpace(oldTableName))
                throw new ArgumentNullException(nameof(oldTableName));
            if (string.IsNullOrWhiteSpace(newTableName))
                throw new ArgumentNullException(nameof(newTableName));

            try
            {
                using var tables = _catalog.Tables;
                using var table = tables[oldTableName];
                table.Name = newTableName;
            }
            catch (Exception e)
            {
                // TODO: Try interating over the collections instead of using Item["Name"].
                throw new Exception($"Cannot rename table '{oldTableName}' to '{newTableName}'.", e);
            }
        }

        public override void RenameColumn(string tableName, string oldColumnName, string newColumnName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(oldColumnName))
                throw new ArgumentNullException(nameof(oldColumnName));
            if (string.IsNullOrWhiteSpace(newColumnName))
                throw new ArgumentNullException(nameof(newColumnName));

            try
            {
                using var tables = _catalog.Tables;
                using var table = tables[tableName];
                using var columns = table.Columns;
                using var column = columns[oldColumnName];
                column.Name = newColumnName;
            }
            catch (Exception e)
            {
                // TODO: Try interating over the collections instead of using Item["Name"].
                throw new Exception($"Cannot rename column '{oldColumnName}' to '{newColumnName}' of table '{tableName}'.", e);
            }
        }

        protected static string GetDataTypeString(DataTypeEnum dataType, bool isIdentity = false)
        {
            switch (dataType)
            {
                case DataTypeEnum.adSmallInt:
                    return "smallint";
                case DataTypeEnum.adInteger:
                    return isIdentity
                        ? "counter"
                        : "integer";
                case DataTypeEnum.adSingle:
                    return "single";
                case DataTypeEnum.adDouble:
                    return "double";
                case DataTypeEnum.adCurrency:
                    return "currency";
                case DataTypeEnum.adDate:
                    return "datetime";
                case DataTypeEnum.adBoolean:
                    return "bit";
                case DataTypeEnum.adDecimal:
                    return "decimal";
                case DataTypeEnum.adTinyInt:
                    return "smallint";
                case DataTypeEnum.adUnsignedTinyInt:
                    return "byte";
                case DataTypeEnum.adUnsignedSmallInt:
                    return "integer";
                case DataTypeEnum.adUnsignedInt:
                    return null;
                case DataTypeEnum.adBigInt:
                    return "bigint";
                case DataTypeEnum.adGUID:
                    return "guid";
                case DataTypeEnum.adBinary:
                    return "binary";
                case DataTypeEnum.adChar:
                case DataTypeEnum.adWChar:
                    return "char";
                case DataTypeEnum.adNumeric:
                    return "decimal";
                case DataTypeEnum.adUserDefined:
                    return null;
                case DataTypeEnum.adDBDate:
                case DataTypeEnum.adDBTime:
                    return "datetime";
                case DataTypeEnum.adDBTimeStamp:
                    return "timestamp";
                case DataTypeEnum.adVarChar:
                    return "varchar";
                case DataTypeEnum.adLongVarChar:
                    return "longchar";
                case DataTypeEnum.adVarWChar:
                    return "varchar";
                case DataTypeEnum.adLongVarWChar:
                    return "longchar";
                case DataTypeEnum.adVarBinary:
                    return "varbinary";
                case DataTypeEnum.adLongVarBinary:
                    return "longbinary";
                case DataTypeEnum.adEmpty:
                case DataTypeEnum.adBSTR:
                case DataTypeEnum.adIDispatch:
                case DataTypeEnum.adError:
                case DataTypeEnum.adVariant:
                case DataTypeEnum.adIUnknown:
                case DataTypeEnum.adUnsignedBigInt:
                case DataTypeEnum.adFileTime:
                case DataTypeEnum.adChapter:
                case DataTypeEnum.adPropVariant:
                case DataTypeEnum.adVarNumeric:
                default:
                    throw new ArgumentOutOfRangeException(nameof(dataType), $"Could not map a data type of '{Enum.GetName(typeof(DataTypeEnum), dataType)}'.");
            }
        }

        protected static int? GetMaxLength(DataTypeEnum dataType, int size)
            => dataType switch
            {
                DataTypeEnum.adBinary => size,
                DataTypeEnum.adChar => size,
                DataTypeEnum.adWChar => size,
                DataTypeEnum.adVarBinary => size,
                DataTypeEnum.adVarChar => size,
                DataTypeEnum.adVarWChar => size,
                DataTypeEnum.adLongVarBinary => null,
                DataTypeEnum.adLongVarChar => null,
                DataTypeEnum.adLongVarWChar => null,
                _ => null
            };

        private static T GetPropertyValue<T>(dynamic properties, string name, T defaultValue = default)
        {
            try
            {
                using var property = properties[name];
                return (T)property.Value;
            }
            catch
            {
#if DEBUG
                var propertyMap = GetProperties(properties);
#endif
                return defaultValue;
            }
        }

        protected static Dictionary<string, object> GetProperties(dynamic properties)
        {
            var propertyMap = new Dictionary<string, object>();
            var propertyCount = properties.Count;
            for (var k = 0; k < propertyCount; k++)
            {
                string key;
                object value;
                try
                {
                    using var p = properties[k];
                    key = (string)p.Name;
                    value = p.Value;
                    propertyMap.Add(key, value);
                }
                catch
                {
                    // ignored
                }
            }

            return propertyMap;
        }

        public override void Dispose()
        {
            _connection.Dispose();
            _catalog.Dispose();
        }

        protected enum RuleEnum
        {
            adRINone = 0,
            adRICascade = 1,
            adRISetNull = 2,
            adRISetDefault = 3,
        }

        protected enum SortOrderEnum
        {
            adSortAscending = 1,
            adSortDescending = 2,
        }

        protected enum KeyTypeEnum
        {
            adKeyPrimary = 1,
            adKeyForeign = 2,
            adKeyUnique = 3,
        }

        protected enum AllowNullsEnum
        {
            adIndexNullsAllow = 0,
            adIndexNullsDisallow = 1,
            adIndexNullsIgnore = 2,
            adIndexNullsIgnoreAny = 4,
        }

        [Flags]
        protected enum ColumnAttributesEnum
        {
            adColFixed = 1,
            adColNullable = 2,
        }

        protected enum DataTypeEnum
        {
            adEmpty = 0,
            adSmallInt = 2,
            adInteger = 3,
            adSingle = 4,
            adDouble = 5,
            adCurrency = 6,
            adDate = 7,
            adBSTR = 8,
            adIDispatch = 9,
            adError = 10,
            adBoolean = 11,
            adVariant = 12,
            adIUnknown = 13,
            adDecimal = 14,
            adTinyInt = 16,
            adUnsignedTinyInt = 17,
            adUnsignedSmallInt = 18,
            adUnsignedInt = 19,
            adBigInt = 20,
            adUnsignedBigInt = 21,
            adFileTime = 64,
            adGUID = 72,
            adBinary = 128,
            adChar = 129,
            adWChar = 130,
            adNumeric = 131,
            adUserDefined = 132,
            adDBDate = 133,
            adDBTime = 134,
            adDBTimeStamp = 135,
            adChapter = 136,
            adPropVariant = 138,
            adVarNumeric = 139,
            adVarChar = 200,
            adLongVarChar = 201,
            adVarWChar = 202,
            adLongVarWChar = 203,
            adVarBinary = 204,
            adLongVarBinary = 205,
        }

        // https://docs.microsoft.com/en-us/office/client-developer/access/desktop-database-reference/schemaenum
        protected enum SchemaEnum
        {
            adSchemaProviderSpecific = -1,
            adSchemaAsserts = 0,
            adSchemaCatalogs = 1,
            adSchemaCharacterSets = 2,
            adSchemaCollations = 3,
            adSchemaColumns = 4,
            adSchemaCheckConstraints = 5,
            adSchemaConstraintColumnUsage = 6,
            adSchemaConstraintTableUsage = 7,
            adSchemaKeyColumnUsage = 8,
            AdSchemaReferentialConstraints = 9,
            adSchemaTableConstraints = 10,
            adSchemaColumnsDomainUsage = 11,
            adSchemaIndexes = 12,
            adSchemaColumnPrivileges = 13,
            adSchemaTablePrivileges = 14,
            adSchemaUsagePrivileges = 15,
            adSchemaProcedures = 16,
            adSchemaSchemata = 17,
            adSchemaSQLLanguages = 18,
            adSchemaStatistics = 19,
            adSchemaTables = 20,
            adSchemaTranslations = 21,
            adSchemaProviderTypes = 22,
            adSchemaViews = 23,
            adSchemaViewColumnUsage = 24,
            adSchemaViewTableUsage = 25,
            adSchemaProcedureParameters = 26,
            adSchemaForeignKeys = 27,
            adSchemaPrimaryKeys = 28,
            adSchemaProcedureColumns = 29,
            adSchemaDBInfoKeywords = 30,
            adSchemaDBInfoLiterals = 31,
            adSchemaCubes = 32,
            adSchemaDimensions = 33,
            adSchemaHierarchies = 34,
            adSchemaLevels = 35,
            adSchemaMeasures = 36,
            adSchemaProperties = 37,
            adSchemaMembers = 38,
            adSchemaTrustees = 39,
        }

        [Flags]
        protected enum CommandTypeEnum
        {
            adCmdUnspecified = -1,
            adCmdText = 0x00000001,
            adCmdTable = 0x00000002,
            adCmdStoredProc = 0x00000004,
            adCmdUnknown = 0x00000008,
            adCmdFile = 0x00000100,
            adCmdTableDirect = 0x00000200,
        }

        [Flags]
        protected enum ExecuteOptionEnum
        {
            adOptionUnspecified = -1,
            adAsyncExecute = 0x00000010,
            adAsyncFetch = 0x00000020,
            adAsyncFetchNonBlocking = 0x00000040,
            adExecuteNoRecords = 0x00000080,
            adExecuteStream = 0x00000400,
            //adExecuteRecord = ???,
        }

        [Flags]
        protected enum FieldAttributeEnum
        {
            adFldCacheDeferred = 0x1000,
            adFldFixed = 0x10,
            adFldIsChapter = 0x2000,
            adFldIsCollection = 0x40000,
            adFldIsDefaultStream = 0x20000,
            adFldIsNullable = 0x20,
            adFldIsRowURL = 0x10000,
            adFldLong = 0x80,
            adFldMayBeNull = 0x40,
            adFldMayDefer = 0x2,
            adFldNegativeScale = 0x4000,
            adFldRowID = 0x100,
            adFldRowVersion = 0x200,
            adFldUnknownUpdatable = 0x8,
            adFldUnspecified = -1,
            adFldUpdatable = 0x4,
        }
    }
}