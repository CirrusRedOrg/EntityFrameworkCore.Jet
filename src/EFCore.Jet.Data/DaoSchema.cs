using System;
using System.Collections.Generic;
using System.Data;
using EntityFrameworkCore.Jet.Data.JetStoreSchemaDefinition;
using System.Diagnostics;
using System.Linq;

namespace EntityFrameworkCore.Jet.Data
{
    public class DaoSchema : SchemaProvider
    {
        private readonly JetConnection _connection;
        private readonly dynamic _database;
        private readonly dynamic _workspace;
        private readonly dynamic _dbEngine;
        private readonly bool _naturalOnly;

        public DaoSchema(JetConnection connection, bool naturalOnly, bool readOnly)
            : this(connection, readOnly)
        {
            _naturalOnly = naturalOnly;
        }
        
        public DaoSchema(JetConnection connection, bool readOnly)
        {
            _connection = connection;

            _dbEngine = ComObject.CreateFirstFrom(
                Enumerable.Range(12, 6)
                    .Reverse()
                    .Concat(new[] {36})
                    .Select(n => "DAO.DBEngine." + (n * 10).ToString())
                    .ToArray());

            try
            {
                var csb = connection.DataAccessProviderFactory.CreateConnectionStringBuilder();
                csb.ConnectionString = connection.ActiveConnectionString;

                var dataSource = csb.GetDataSource();
                var userId = csb.GetUserId();
                var password = csb.GetPassword();
                var systemDatabase = csb.GetSystemDatabase();
                var databasePassword = csb.GetDatabasePassword();

                if (!string.IsNullOrEmpty(systemDatabase))
                {
                    _dbEngine.SystemDB = systemDatabase;
                }

                _workspace = _dbEngine.CreateWorkspace(string.Empty, userId ?? "Admin", password ?? string.Empty, /*WorkspaceTypeEnum.dbUseJet*/ 2);

                try
                {
                    _database = _workspace.OpenDatabase(
                        dataSource,
                        /* Exclusive */ false,
                        /* ReadOnly */ readOnly,
                        "MS Access" + (string.IsNullOrEmpty(databasePassword)
                            ? null
                            : $";PWD={databasePassword}"));
                }
                catch
                {
                    _workspace.Dispose();
                    throw;
                }
            }
            catch
            {
                _dbEngine.Dispose();
                throw;
            }
        }

        public override DataTable GetTables()
        {
            var dataTable = SchemaTables.GetTablesDataTable();

            using var tableDefs = _database.TableDefs;
            var tableCount = (int) tableDefs.Count;

            for (var i = 0; i < tableCount; i++)
            {
                using var tableDef = tableDefs[i];

                var tableName = (string) tableDef.Name;
                var tableAttributes = (int) tableDef.Attributes;

                string tableType;
                if (IsInternalTableByName(tableName))
                {
                    tableType = "INTERNAL TABLE";
                }
                else if (IsSystemTableByName(tableName) ||
                         (tableAttributes & (int) TableDefAttributeEnum.dbSystemObject) == (int) TableDefAttributeEnum.dbSystemObject ||
                         (tableAttributes & (1 << 31)) == 1 << 31)
                {
                    tableType = "SYSTEM TABLE";
                }
                else
                {
                    tableType = "BASE TABLE";
                }
                
                var validationRule = (string) tableDef.ValidationRule;
                validationRule = string.IsNullOrEmpty(validationRule)
                    ? null
                    : validationRule;

                var validationText = (string) tableDef.ValidationText;
                validationText = string.IsNullOrEmpty(validationText)
                    ? null
                    : validationText;

                dataTable.Rows.Add(
                    tableName,
                    tableType,
                    validationRule,
                    validationText);
            }

            using var queryDefs = _database.QueryDefs;
            var queryCount = (int) queryDefs.Count;

            for (var i = 0; i < queryCount; i++)
            {
                using var queryDef = queryDefs[i];
                using var parameters = queryDef.Parameters;
                var parameterCount = parameters.Count;

                // If the QueryDef contains parameters, we will treat it as a stored procedure.
                if (parameterCount <= 0)
                {
                    var tableName = (string) queryDef.Name;
                    var type = (QueryDefTypeEnum) queryDef.Type;

                    if (type == QueryDefTypeEnum.dbQSelect ||
                        type == QueryDefTypeEnum.dbQSetOperation ||
                        type == QueryDefTypeEnum.dbQCrosstab ||
                        type == QueryDefTypeEnum.dbQSQLPassThrough)
                    {
                        dataTable.Rows.Add(
                            tableName,
                            "VIEW",
                            null,
                            null);
                    }
                }
            }

            dataTable.AcceptChanges();
            return dataTable;
        }

        public override DataTable GetColumns()
        {
            var dataTable = SchemaTables.GetColumnsDataTable();

            // There is no way to get the scale of a decimal with DAO. Looks like someone at Microsoft just forgot to
            // implement it.
            // Therefore, either ADO has to be used, or DAO together with the GetSchema()
            // method (that contains precision and scale, but no default value when using ODBC, because again, looks
            // like someone at Microsoft just forgot to implement it).
            Dictionary<(string TableName, string ColumnName), int?>? numericScales = null;
            
            if (!_naturalOnly)
            {
                var schemaTable = _connection.InnerConnection.GetSchema("Columns");
                numericScales = schemaTable.Rows
                    .Cast<DataRow>()
                    .ToDictionary(
                        t => (TableName: (string) t["TABLE_NAME"], ColumnName: (string) t["COLUMN_NAME"]),
                        t => t.Table.Columns.Contains("DECIMAL_DIGITS")
                            ? t["DECIMAL_DIGITS"] != DBNull.Value
                                ? (int?) (short?) t["DECIMAL_DIGITS"]
                                : null
                            : t["NUMERIC_SCALE"] != DBNull.Value
                                ? (int?) (short?) t["NUMERIC_SCALE"]
                                : null);
            }

            var objectDefsCollection = new[]
            {
                (Collection: _database.TableDefs, Tables: true),
                (Collection: _database.QueryDefs, Tables: false),
            };

            try
            {
                foreach (var objectDefs in objectDefsCollection)
                {
                    var objectDefCount = objectDefs.Collection.Count;

                    for (var i = 0; i < objectDefCount; i++)
                    {
                        using var objectDef = objectDefs.Collection[i];
                        var tableName = (string) objectDef.Name;

                        using var fields = objectDef.Fields;
                        var fieldCount = (int) fields.Count;

                        for (var j = 0; j < fieldCount; j++)
                        {
                            using var field = fields[j];

                            var attributes = (FieldAttributeEnum) field.Attributes;
                            var isIdentity = (attributes & FieldAttributeEnum.dbAutoIncrField) == FieldAttributeEnum.dbAutoIncrField;

                            // Looks like there is no way to get the seed and increment values through DAO.
                            var seed = isIdentity && !_naturalOnly ? (int?)1 : null;
                            var increment = isIdentity && !_naturalOnly ? (int?)1 : null;
                            
                            var columnName = (string) field.Name;
                            var ordinalPosition = (int) field.OrdinalPosition;
                            var dataType = (DataTypeEnum) field.Type;
                            var dataTypeString = GetDataTypeString(dataType, isIdentity);
                            var nullable = !(bool) field.Required;
                            var collatingOrder = (int) field.CollatingOrder;
                            var numericPrecision = dataType == DataTypeEnum.dbDecimal || dataType == DataTypeEnum.dbNumeric
                                ? (int?) collatingOrder
                                : null;

                            int? numericScale = numericScales != null &&
                                                (dataType == DataTypeEnum.dbDecimal || dataType == DataTypeEnum.dbNumeric)
                                ? numericScales[(tableName, columnName)]
                                : null;

                            var size = (int) field.Size;
                            var length = GetMaxLength(dataType, size);
                            var defaultValue = !string.IsNullOrEmpty(field.DefaultValue)
                                ? (string) field.DefaultValue
                                : null;

                            var validationRule = (string) field.ValidationRule;
                            validationRule = string.IsNullOrEmpty(validationRule)
                                ? null
                                : validationRule;

                            var validationText = (string) field.ValidationText;
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
                }
            }
            finally
            {
                foreach (var objectDefs in objectDefsCollection)
                {
                    objectDefs.Collection.Dispose();
                }
            }

            dataTable.AcceptChanges();
            return dataTable;
        }

        public override DataTable GetIndexes()
        {
            var dataTable = SchemaTables.GetIndexesDataTable();

            using var tableDefs = _database.TableDefs;
            var tableDefCount = (int) tableDefs.Count;

            for (var i = 0; i < tableDefCount; i++)
            {
                using var tableDef = tableDefs[i];
                var tableName = (string) tableDef.Name;

                try
                {
                    using var indexes = tableDef.Indexes;
                    var indexCount = (int) indexes.Count;

                    for (var j = 0; j < indexCount; j++)
                    {
                        using var index = indexes[j];

                        var indexName = (string) index.Name;
                        var isPrimaryKey = (bool) index.Primary;
                        var isUnique = (bool) index.Unique;
                        var isNullable = !(bool) index.Required;
                        var ignoreNulls = (bool) index.IgnoreNulls;

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
                catch
                {
                    var tableAttributes = (int) tableDef.Attributes;
                    var isSystemTable = (tableAttributes & (int) TableDefAttributeEnum.dbSystemObject) == (int) TableDefAttributeEnum.dbSystemObject ||
                                        (tableAttributes & (1 << 31)) == 1 << 31 ||
                                        IsSystemTableByName(tableName);

                    Debug.Assert(isSystemTable);

                    // TODO: Otherwise, output warning.
                }
            }

            dataTable.AcceptChanges();
            return dataTable;
        }

        public override DataTable GetIndexColumns()
        {
            var dataTable = SchemaTables.GetIndexColumnsDataTable();

            using var tableDefs = _database.TableDefs;
            var tableDefCount = (int) tableDefs.Count;

            for (var i = 0; i < tableDefCount; i++)
            {
                using var tableDef = tableDefs[i];
                var tableName = (string) tableDef.Name;

                try
                {
                    using var indexes = tableDef.Indexes;
                    var indexCount = (int) indexes.Count;

                    for (var j = 0; j < indexCount; j++)
                    {
                        using var index = indexes[j];
                        var indexName = (string) index.Name;

                        using var fields = index.Fields;
                        var fieldCount = (int) fields.Count;

                        for (var k = 0; k < fieldCount; k++)
                        {
                            using var field = fields[k];

                            var fieldName = (string) field.Name;
                            var ordinalPosition = k;
                            var attributes = (FieldAttributeEnum) field.Attributes;
                            var isDescending = (attributes & FieldAttributeEnum.dbDescending) == FieldAttributeEnum.dbDescending;

                            dataTable.Rows.Add(
                                tableName,
                                indexName,
                                ordinalPosition,
                                fieldName,
                                isDescending);
                        }
                    }
                }
                catch
                {
                    var tableAttributes = (int) tableDef.Attributes;
                    var isSystemTable = (tableAttributes & (int) TableDefAttributeEnum.dbSystemObject) == (int) TableDefAttributeEnum.dbSystemObject ||
                                        (tableAttributes & (1 << 31)) == 1 << 31 ||
                                        IsSystemTableByName(tableName);

                    Debug.Assert(isSystemTable);

                    // TODO: Otherwise, output warning.
                }
            }

            dataTable.AcceptChanges();
            return dataTable;
        }

        public override DataTable GetRelations()
        {
            var dataTable = SchemaTables.GetRelationsDataTable();

            using var relations = _database.Relations;
            var relationCount = (int) relations.Count;

            for (var i = 0; i < relationCount; i++)
            {
                using var relation = relations[i];

                var relationName = (string) relation.Name;
                var referencingTableName = (string) relation.ForeignTable;
                var principalTableName = (string) relation.Table;
                var attributes = (RelationAttributeEnum) relation.Attributes;

                var relationType = (attributes & RelationAttributeEnum.dbRelationUnique) == RelationAttributeEnum.dbRelationUnique
                    ? "ONE"
                    : "MANY";

                var onUpdate = (attributes & RelationAttributeEnum.dbRelationUpdateCascade) == RelationAttributeEnum.dbRelationUpdateCascade
                    ? "CASCADE"
                    : "NO ACTION";

                var onDelete = (attributes & RelationAttributeEnum.dbRelationDeleteCascade) == RelationAttributeEnum.dbRelationDeleteCascade
                    ? "CASCADE"
                    : "NO ACTION";

                var isEnforced = (attributes & RelationAttributeEnum.dbRelationDontEnforce) != RelationAttributeEnum.dbRelationDontEnforce;

                var isInherited = (attributes & RelationAttributeEnum.dbRelationInherited) != RelationAttributeEnum.dbRelationInherited;

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

            dataTable.AcceptChanges();
            return dataTable;
        }

        public override DataTable GetRelationColumns()
        {
            var dataTable = SchemaTables.GetRelationColumnsDataTable();

            using var relations = _database.Relations;
            var relationCount = (int) relations.Count;

            for (var i = 0; i < relationCount; i++)
            {
                using var relation = relations[i];
                var relationName = (string) relation.Name;

                using var fields = relation.Fields;
                var fieldCount = (int) fields.Count;

                for (var j = 0; j < fieldCount; j++)
                {
                    using var field = fields[j];

                    var principalColumnName = (string) field.Name;
                    var referencingColumnName = (string) field.ForeignName;

                    dataTable.Rows.Add(
                        relationName,
                        referencingColumnName,
                        principalColumnName,
                        j + 1);
                }
            }

            dataTable.AcceptChanges();
            return dataTable;
        }

        // DAO does not support CHECK CONSTRAINTs. Only ADOX does.
        public override DataTable GetCheckConstraints()
            => SchemaTables.GetCheckConstraintsDataTable();
        
        public override void EnsureDualTable()
        {
            using var tableDefs = _database.TableDefs;

            try
            {
                using var tableDef = tableDefs[JetConnection.DefaultDualTableName];
            }
            catch
            {
                _database.Execute($@"CREATE TABLE `{JetConnection.DefaultDualTableName}` (`ID` INTEGER NOT NULL CONSTRAINT `PrimaryKey` PRIMARY KEY)", RecordsetOptionEnum.dbFailOnError);
                _database.Execute($@"INSERT INTO `{JetConnection.DefaultDualTableName}` (`ID`) VALUES (1)", RecordsetOptionEnum.dbFailOnError);

                // Check constraints are not supported by DAO. They are supported by ADOX.
                // database.Execute($@"ALTER TABLE [{JetConnection.DefaultDualTableName}] ADD CONSTRAINT SingleRecord CHECK ([ID] = 1)", RecordsetOptionEnum.dbFailOnError);

                tableDefs.Refresh();
                using var tableDef = tableDefs[JetConnection.DefaultDualTableName];
                tableDef.ValidationRule = "[ID] = 1"; // not as good as a CHECK CONSTRAINT, but better than nothing

                var attributes = (TableDefAttributeEnum) tableDef.Attributes;
                attributes |= TableDefAttributeEnum.dbSystemObject;
                tableDef.Attributes = attributes;
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
                using var tableDefs = _database.TableDefs;
                using var tableDef = tableDefs[oldTableName];
                tableDef.Name = newTableName;
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
                using var tableDefs = _database.TableDefs;
                using var tableDef = tableDefs[tableName];
                using var fields = tableDef.Fields;
                using var field = fields[oldColumnName];
                field.Name = newColumnName;
            }
            catch (Exception e)
            {
                // TODO: Try interating over the collections instead of using Item["Name"].
                throw new Exception($"Cannot rename column '{oldColumnName}' to '{newColumnName}' of table '{tableName}'.", e);
            }
        }

        protected static string GetDataTypeString(DataTypeEnum dataType, bool isIdentity = false)
            => dataType switch
            {
                DataTypeEnum.dbBoolean => "bit",
                DataTypeEnum.dbByte => "byte",
                DataTypeEnum.dbInteger => "smallint",
                DataTypeEnum.dbLong => isIdentity
                    ? "counter"
                    : "integer",
                DataTypeEnum.dbCurrency => "currency",
                DataTypeEnum.dbSingle => "single",
                DataTypeEnum.dbDouble => "double",
                DataTypeEnum.dbDate => "datetime",
                DataTypeEnum.dbBinary => "binary",
                DataTypeEnum.dbText => "varchar",
                DataTypeEnum.dbLongBinary => "longbinary",
                DataTypeEnum.dbMemo => "longchar",
                DataTypeEnum.dbGUID => "uniqueidentifier",
                DataTypeEnum.dbBigInt => "integer", // TODO
                DataTypeEnum.dbVarBinary => "varbinary",
                DataTypeEnum.dbChar => "char",
                DataTypeEnum.dbNumeric => "decimal", // CHECK: https://docs.microsoft.com/en-us/previous-versions/office/developer/office2000/aa140015(v=office.10)#the-numeric-data-types
                DataTypeEnum.dbDecimal => "decimal",
                DataTypeEnum.dbFloat => "double", // CHECK
                DataTypeEnum.dbTime => "datetime", // CHECK
                DataTypeEnum.dbTimeStamp => "timestamp",
                _ => throw new ArgumentOutOfRangeException(nameof(dataType))
            };

        protected static int? GetMaxLength(DataTypeEnum dataType, int size)
            => dataType switch
            {
                DataTypeEnum.dbBinary => size,
                DataTypeEnum.dbText => size,
                DataTypeEnum.dbVarBinary => size,
                DataTypeEnum.dbChar => size,
                DataTypeEnum.dbLongBinary => null,
                DataTypeEnum.dbMemo => null,
                _ => null
            };

        protected static T GetPropertyValue<T>(dynamic properties, string name, T defaultValue = default)
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
        
        public Dictionary<(string TableName, string ColumnName), (int OrdinalPosition, bool Nullable)> GetOrdinalPositionsAndNullables()
        {
            var ordinalPositions = new Dictionary<(string TableName, string ColumnName), (int OrdinalPosition, bool Nullable)>();
            
            var objectDefsCollection = new[]
            {
                _database.TableDefs,
                _database.QueryDefs,
            };

            try
            {
                foreach (var objectDefs in objectDefsCollection)
                {
                    var objectDefCount = objectDefs.Count;

                    for (var i = 0; i < objectDefCount; i++)
                    {
                        using var objectDef = objectDefs[i];
                        var tableName = (string) objectDef.Name;

                        using var fields = objectDef.Fields;
                        var fieldCount = (int) fields.Count;

                        for (var j = 0; j < fieldCount; j++)
                        {
                            using var field = fields[j];

                            var columnName = (string) field.Name;
                            var ordinalPosition = (int) field.OrdinalPosition;
                            var nullable = !(bool) field.Required;
                            
                            ordinalPositions.Add((tableName, columnName), (ordinalPosition, nullable));
                        }
                    }
                }
            }
            finally
            {
                foreach (var objectDefs in objectDefsCollection)
                {
                    objectDefs.Dispose();
                }
            }

            return ordinalPositions;
        }

        public Dictionary<string,(string relationType,bool isEnforced,bool isInherited)> GetRelationTypes()
        {
            var result = new Dictionary<string, (string,bool,bool)>();
            using var relations = _database.Relations;
            var relationCount = (int)relations.Count;

            for (var i = 0; i < relationCount; i++)
            {
                using var relation = relations[i];

                var relationName = (string)relation.Name;
                var attributes = (RelationAttributeEnum)relation.Attributes;

                var relationType = (attributes & RelationAttributeEnum.dbRelationUnique) == RelationAttributeEnum.dbRelationUnique
                    ? "ONE"
                    : "MANY";

                var isEnforced = (attributes & RelationAttributeEnum.dbRelationDontEnforce) != RelationAttributeEnum.dbRelationDontEnforce;

                var isInherited = (attributes & RelationAttributeEnum.dbRelationInherited) != RelationAttributeEnum.dbRelationInherited;

                result.Add(relationName, (relationType,isEnforced,isInherited));
            }

            return result;
        }

        public override void Dispose()
        {
            _database.Dispose();
            _workspace.Dispose();
            _dbEngine.Dispose();
        }

        [Flags]
        protected enum TableDefAttributeEnum
        {
            dbSystemObject = (1 << 31) | 0x0000002, // 0x80000002, this actually means hidden
            dbHiddenObject = 0x00000001, // does not really mean hidden, but temporary (which will also be hidden)
            dbAttachExclusive = 0x00010000,
            dbAttachSavePWD = 0x00020000,
            dbAttachedODBC = 0x20000000,
            dbAttachedTable = 0x40000000,
        }

        protected enum QueryDefTypeEnum
        {
            dbQSelect = 0,
            dbQCrosstab = 0x00000010,
            dbQDelete = 0x00000020,
            dbQUpdate = 0x00000030,
            dbQAppend = 0x00000040,
            dbQMakeTable = 0x00000050,
            dbQDDL = 0x00000060,
            dbQSQLPassThrough = 0x00000070,
            dbQSetOperation = 0x00000080,
            dbQSPTBulk = 0x00000090,
            dbQCompound = 0x000000A0,
            dbQProcedure = 0x000000E0,
            dbQAction = 0x000000F0,
        }

        [Flags]
        protected enum FieldAttributeEnum
        {
            dbDescending = 0x00000001,
            dbFixedField = 0x00000001,
            dbVariableField = 0x00000002,
            dbAutoIncrField = 0x00000010,
            dbUpdatableField = 0x00000020,
            dbSystemField = 0x00002000,
            dbHyperlinkField = 0x00008000,
        }

        protected enum DataTypeEnum : short
        {
            dbBoolean = 1,
            dbByte = 2,
            dbInteger = 3,
            dbLong = 4,
            dbCurrency = 5,
            dbSingle = 6,
            dbDouble = 7,
            dbDate = 8,
            dbBinary = 9,
            dbText = 10,
            dbLongBinary = 11,
            dbMemo = 12,
            dbGUID = 15,
            dbBigInt = 16,
            dbVarBinary = 17,
            dbChar = 18,
            dbNumeric = 19,
            dbDecimal = 20,
            dbFloat = 21,
            dbTime = 22,
            dbTimeStamp = 23,
        }

        [Flags]
        protected enum RelationAttributeEnum
        {
            dbRelationUnique = 0x00000001,
            dbRelationDontEnforce = 0x00000002,
            dbRelationInherited = 0x00000004,
            dbRelationUpdateCascade = 0x00000100,
            dbRelationDeleteCascade = 0x00001000,
            dbRelationLeft = 0x01000000,
            dbRelationRight = 0x02000000,
        }
        
        [Flags]
        protected enum RecordsetOptionEnum
        {
            dbDenyWrite = 0x00000001,
            dbDenyRead = 0x00000002,
            dbReadOnly = 0x00000004,
            dbAppendOnly = 0x00000008,
            dbInconsistent = 0x00000010,
            dbConsistent = 0x00000020,
            dbSQLPassThrough = 0x00000040,
            dbFailOnError = 0x00000080,
            dbForwardOnly = 0x00000100,
            dbSeeChanges = 0x00000200,
            dbRunAsync = 0x00000400,
            dbExecDirect = 0x00000800,
        }
    }
}