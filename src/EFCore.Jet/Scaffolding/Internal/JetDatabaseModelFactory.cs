// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Jet;
using System.Diagnostics;
using System.Linq;
using EntityFrameworkCore.Jet.Internal;
using EntityFrameworkCore.Jet.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata.Internal;
using EntityFrameworkCore.Jet.Utilities;

namespace EntityFrameworkCore.Jet.Scaffolding.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetDatabaseModelFactory : IDatabaseModelFactory
    {
        private DbConnection _connection;
        private Version _serverVersion;
        private HashSet<string> _tablesToInclude;
        private HashSet<string> _schemasToInclude;
        private HashSet<string> _selectedSchemas;
        private HashSet<string> _selectedTables;
        private DatabaseModel _databaseModel;
        private Dictionary<string, DatabaseTable> _tables;
        private Dictionary<string, DatabaseColumn> _tableColumns;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        private static string SchemaQualifiedKey([NotNull] string name, [CanBeNull] string schema = null) => "[" + (schema ?? "") + "].[" + name + "]";

        private static string TableKey(DatabaseTable table) => SchemaQualifiedKey(table.Name, table.Schema);
        private static string ColumnKey(DatabaseTable table, string columnName) => TableKey(table) + ".[" + columnName + "]";

        private static readonly ISet<string> _dateTimePrecisionTypes = new HashSet<string> { "datetimeoffset", "datetime2", "time" };

        // see https://msdn.microsoft.com/en-us/library/ff878091.aspx

        private static readonly List<string> _schemaPatterns = new List<string>
        {
            "{schema}",
            "[{schema}]"
        };

        private static readonly List<string> _tablePatterns = new List<string>
        {
            "{schema}.{table}",
            "[{schema}].[{table}]",
            "{schema}.[{table}]",
            "[{schema}].{table}",
            "{table}",
            "[{table}]"
        };

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetDatabaseModelFactory([NotNull] IDiagnosticsLogger<DbLoggerCategory.Scaffolding> logger)
        {
            Check.NotNull(logger, nameof(logger));

            Logger = logger;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IDiagnosticsLogger<DbLoggerCategory.Scaffolding> Logger { get; }

        private void ResetState()
        {
            _connection = null;
            _serverVersion = null;
            _selectedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _selectedSchemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _tablesToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _schemasToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _databaseModel = new DatabaseModel();
            _tables = new Dictionary<string, DatabaseTable>();
            _tableColumns = new Dictionary<string, DatabaseColumn>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual DatabaseModel Create(string connectionString, IEnumerable<string> tables, IEnumerable<string> schemas)
        {
            Check.NotEmpty(connectionString, nameof(connectionString));
            Check.NotNull(tables, nameof(tables));
            Check.NotNull(schemas, nameof(schemas));

            using (var connection = new JetConnection(connectionString))
            {
                return Create(connection, tables, schemas);
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual DatabaseModel Create(DbConnection connection, IEnumerable<string> tables, IEnumerable<string> schemas)
        {
            Check.NotNull(connection, nameof(connection));
            Check.NotNull(tables, nameof(tables));
            Check.NotNull(schemas, nameof(schemas));

            ResetState();

            _connection = connection;

            var connectionStartedOpen = _connection.State == ConnectionState.Open;
            if (!connectionStartedOpen)
            {
                _connection.Open();
            }
            try
            {
                foreach (var schema in schemas)
                {
                    _schemasToInclude.Add(schema);
                }

                foreach (var table in tables)
                {
                    _tablesToInclude.Add(table);
                }

                _databaseModel.DatabaseName = _connection.Database;

                Version.TryParse(_connection.ServerVersion, out _serverVersion);

                GetDefaultSchema();
                GetTables();
                GetColumns();
                GetPrimaryKeys();
                GetUniqueConstraints();
                GetIndexes();
                GetForeignKeys();

                CheckSelectionsMatched();

                return _databaseModel;
            }
            finally
            {
                if (!connectionStartedOpen)
                {
                    _connection.Close();
                }
            }
        }

        private void CheckSelectionsMatched()
        {
            foreach (var schema in _schemasToInclude.Except(_selectedSchemas, StringComparer.OrdinalIgnoreCase))
            {
                Logger.MissingSchemaWarning(schema);
            }

            foreach (var table in _tablesToInclude.Except(_selectedTables, StringComparer.OrdinalIgnoreCase))
            {
                Logger.MissingTableWarning(table);
            }
        }


        private void GetDefaultSchema()
        {
            _databaseModel.DefaultSchema = "Jet";
        }



        private void GetTables()
        {
            var command = _connection.CreateCommand();
            command.CommandText =
                $"SHOW TABLES WHERE Name <> '{HistoryRepository.DefaultTableName}'";
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var table = new DatabaseTable
                    {
                        Database = _databaseModel,
                        Schema = "Jet",
                        Name = reader.GetValueOrDefault<string>("Name")
                    };

                    if (AllowsTable(table.Schema, table.Name))
                    {
                        Logger.TableFound(DisplayName(table.Schema, table.Name));
                        _databaseModel.Tables.Add(table);
                        _tables[TableKey(table)] = table;
                    }
                    else
                    {
                        Logger.TableSkipped(DisplayName(table.Schema, table.Name));
                    }
                }
            }
        }

        private bool AllowsTable(string schema, string table)
        {
            if (_schemasToInclude.Count == 0
                && _tablesToInclude.Count == 0)
            {
                return true;
            }

            foreach (var schemaPattern in _schemaPatterns)
            {
                var key = schemaPattern.Replace("{schema}", schema);
                if (_schemasToInclude.Contains(key))
                {
                    _selectedSchemas.Add(schema);
                    return true;
                }
            }

            foreach (var tablePattern in _tablePatterns)
            {
                var key = tablePattern.Replace("{schema}", schema).Replace("{table}", table);
                if (_tablesToInclude.Contains(key))
                {
                    _selectedTables.Add(key);
                    return true;
                }
            }

            return false;
        }

        private void GetColumns()
        {
            var command = _connection.CreateCommand();
            command.CommandText = @"
SHOW TABLECOLUMNS
WHERE 
    Table <> '" + HistoryRepository.DefaultTableName + @"'
ORDER BY 
    Table, Ordinal";

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var schemaName = "Jet";
                    var tableName = reader.GetValueOrDefault<string>("Table");
                    var columnName = reader.GetValueOrDefault<string>("Name");
                    var dataTypeName = reader.GetValueOrDefault<string>("TypeName");
                    var dataTypeSchemaName = "Jet";
                    var ordinal = reader.GetValueOrDefault<int>("Ordinal");
                    var nullable = reader.GetValueOrDefault<bool>("IsNullable");
                    var primaryKeyOrdinal = reader.GetValueOrDefault<bool>("IsKey") ? (int?)reader.GetValueOrDefault<int>("Ordinal") : null;
                    var defaultValue = reader.GetValueOrDefault<string>("Default");
                    var computedValue = (string)null;
                    var precision = reader.GetValueOrDefault<int?>("Precision");
                    var scale = reader.GetValueOrDefault<int?>("Scale");
                    var maxLength = reader.GetValueOrDefault<int?>("MaxLength");
                    var isIdentity = reader.GetValueOrDefault<bool>("IsIdentity");

                    Logger.ColumnFound(
                        DisplayName(schemaName, tableName), columnName, DisplayName(dataTypeSchemaName, dataTypeName), ordinal, nullable,
                        primaryKeyOrdinal, defaultValue, computedValue, precision, scale, maxLength, isIdentity);

                    if (!_tables.TryGetValue(SchemaQualifiedKey(tableName, schemaName), out var table))
                    {
                        Logger.ColumnSkipped(DisplayName(schemaName, tableName), columnName);
                        continue;
                    }

                    string storeType;
                    string underlyingStoreType;
                    storeType = GetStoreType(dataTypeName, precision, scale, maxLength);
                    underlyingStoreType = null;

                    if (defaultValue == "(NULL)")
                    {
                        defaultValue = null;
                    }

                    var column = new DatabaseColumn
                    {
                        Table = table,
                        Name = columnName,
                        StoreType = storeType,
                        IsNullable = nullable,
                        DefaultValueSql = defaultValue,
                        ComputedColumnSql = computedValue,
                        ValueGenerated = isIdentity
                            ? ValueGenerated.OnAdd
                            : (underlyingStoreType ?? storeType) == "rowversion"
                                ? ValueGenerated.OnAddOrUpdate
                                : default(ValueGenerated?)
                    };

                    if ((underlyingStoreType ?? storeType) == "rowversion")
                    {
                        column[ScaffoldingAnnotationNames.ConcurrencyToken] = true;
                    }

                    column.SetUnderlyingStoreType(underlyingStoreType);

                    table.Columns.Add(column);
                    _tableColumns.Add(ColumnKey(table, column.Name), column);
                }
            }
        }

        private string GetStoreType(string dataTypeName, int? precision, int? scale, int? maxLength)
        {
            if (dataTypeName == "decimal"
                || dataTypeName == "numeric")
            {
                return $"{dataTypeName}({precision}, {scale.GetValueOrDefault(0)})";
            }
            if (_dateTimePrecisionTypes.Contains(dataTypeName)
                && scale != null)
            {
                return $"{dataTypeName}({scale})";
            }
            if (maxLength == -1)
            {
                if (string.Equals(dataTypeName, "varchar", StringComparison.InvariantCultureIgnoreCase))
                    return "text";
                else if (string.Equals(dataTypeName, "varbinary", StringComparison.InvariantCultureIgnoreCase))
                    return "image";
                else
                    throw new InvalidOperationException("Unexpected type " + dataTypeName);
            }

            if (dataTypeName == "timestamp")
                return "rowversion";

            if (dataTypeName == "bit")
            {
                return "bit";
            }

            if (maxLength.HasValue && maxLength > 0)
                return $"{dataTypeName}({maxLength.Value})";

            return dataTypeName;
        }

        private void GetPrimaryKeys()
        {
            var command = _connection.CreateCommand();
            command.CommandText = @"
SELECT 
    [#Tables].SchemaName,
    [#Tables].NAME AS TableName,
    [#Constraints].NAME AS IndexName,
    [#Constraints].ConstraintType,
    [#TableColumns].NAME AS ColumnName,
    [#TableColumns].Ordinal AS KeyOrdinal
FROM (
    (SHOW CONSTRAINTCOLUMNS) INNER JOIN (
        (SHOW CONSTRAINTS) INNER JOIN (SHOW TABLES)
            ON [#Constraints].ParentId = [#Tables].Id
        )
        ON [#ConstraintColumns].ConstraintId = [#Constraints].Id
    )
INNER JOIN (SHOW TABLECOLUMNS)
    ON [#ConstraintColumns].ColumnId = [#TableColumns].Id
WHERE 
    ConstraintType = 'PRIMARY KEY' AND
    [#Tables].NAME <> '" + HistoryRepository.DefaultTableName + @"'
ORDER BY 
    [#Tables].NAME, 
    [#Constraints].NAME, 
    [#TableColumns].Ordinal";

            using (var reader = command.ExecuteReader())
            {
                DatabasePrimaryKey primaryKey = null;
                while (reader.Read())
                {
                    var schemaName = "Jet";
                    var tableName = reader.GetValueOrDefault<string>("TableName");
                    var indexName = reader.GetValueOrDefault<string>("IndexName");
                    var typeDesc = reader.GetValueOrDefault<string>("ConstraintType");
                    var columnName = reader.GetValueOrDefault<string>("ColumnName");
                    var indexOrdinal = reader.GetValueOrDefault<int>("KeyOrdinal");

                    Logger.IndexColumnFound(
                        DisplayName(schemaName, tableName), indexName, true, columnName, indexOrdinal);

                    Debug.Assert(primaryKey == null || primaryKey.Table != null);
                    if (primaryKey == null
                        || primaryKey.Name != indexName
                        // ReSharper disable once PossibleNullReferenceException
                        || primaryKey.Table.Name != tableName
                        || primaryKey.Table.Schema != schemaName)
                    {
                        if (!_tables.TryGetValue(SchemaQualifiedKey(tableName, schemaName), out var table))
                        {
                            Logger.IndexTableMissingWarning(indexName, DisplayName(schemaName, tableName));
                            continue;
                        }

                        primaryKey = new DatabasePrimaryKey
                        {
                            Table = table,
                            Name = indexName
                        };

                        if (typeDesc == "NONCLUSTERED")
                        {
                            primaryKey[JetAnnotationNames.Clustered] = false;
                        }

                        Debug.Assert(table.PrimaryKey == null);
                        table.PrimaryKey = primaryKey;
                    }

                    if (_tableColumns.TryGetValue(ColumnKey(primaryKey.Table, columnName), out var column))
                    {
                        primaryKey.Columns.Add(column);
                    }
                }
            }
        }

        private void GetUniqueConstraints()
        {
            var command = _connection.CreateCommand();
            command.CommandText = @"
SELECT 
    [#Constraints].ParentId,
    [#Constraints].NAME,
    [#Constraints].ConstraintType,
    [#TableColumns].NAME AS ColumnName,
    [#TableColumns].Ordinal AS ColumnOrdinal
FROM (
    (SHOW ConstraintColumns) INNER JOIN (SHOW Constraints)
        ON [#ConstraintColumns].ConstraintId = [#Constraints].Id
    )
INNER JOIN (SHOW TableColumns)
    ON [#ConstraintColumns].ColumnId = [#TableColumns].Id
WHERE
    ConstraintType = 'UNIQUE' AND
    [#Constraints].ParentId <> '" + HistoryRepository.DefaultTableName + @"'
ORDER BY 
    [#Constraints].ParentId, 
    [#Constraints].NAME, 
    [#TableColumns].Ordinal";

            using (var reader = command.ExecuteReader())
            {
                DatabaseUniqueConstraint uniqueConstraint = null;
                while (reader.Read())
                {
                    var schemaName = "Jet";
                    var tableName = reader.GetValueOrDefault<string>("ParentId");
                    var indexName = reader.GetValueOrDefault<string>("Name");
                    var typeDesc = reader.GetValueOrDefault<string>("ConstraintType");
                    var columnName = reader.GetValueOrDefault<string>("ColumnName");
                    var indexOrdinal = reader.GetValueOrDefault<int>("ColumnOrdinal");

                    Logger.IndexColumnFound(
                        DisplayName(schemaName, tableName), indexName, true, columnName, indexOrdinal);

                    Debug.Assert(uniqueConstraint == null || uniqueConstraint.Table != null);
                    if (uniqueConstraint == null
                        || uniqueConstraint.Name != indexName
                        // ReSharper disable once PossibleNullReferenceException
                        || uniqueConstraint.Table.Name != tableName
                        || uniqueConstraint.Table.Schema != schemaName)
                    {
                        if (!_tables.TryGetValue(SchemaQualifiedKey(tableName, schemaName), out var table))
                        {
                            Logger.IndexTableMissingWarning(indexName, DisplayName(schemaName, tableName));
                            continue;
                        }

                        uniqueConstraint = new DatabaseUniqueConstraint
                        {
                            Table = table,
                            Name = indexName
                        };

                        if (typeDesc == "CLUSTERED")
                        {
                            uniqueConstraint[JetAnnotationNames.Clustered] = true;
                        }

                        table.UniqueConstraints.Add(uniqueConstraint);
                    }

                    if (_tableColumns.TryGetValue(ColumnKey(uniqueConstraint.Table, columnName), out var column))
                    {
                        uniqueConstraint.Columns.Add(column);
                    }
                }
            }
        }

        private void GetIndexes()
        {
            var command = _connection.CreateCommand();
            command.CommandText = @"
SELECT 
    [#Indexes].TABLE,
    [#Indexes].NAME,
    [#Indexes].IsUnique,
    [#IndexColumns].NAME AS ColumnName,
    [#IndexColumns].Ordinal
FROM 
    (SHOW IndexColumns)
INNER JOIN
    (SHOW Indexes)
        ON [#IndexColumns].ParentId = [#Indexes].Id
WHERE 
    IsPrimary = False AND
    IsUnique = False AND 
    [#Indexes].Table <> '" + HistoryRepository.DefaultTableName + @"'
ORDER BY 
    [#Indexes].Table,
    [#Indexes].Name,
    Ordinal";

            using (var reader = command.ExecuteReader())
            {
                DatabaseIndex index = null;
                while (reader.Read())
                {
                    var schemaName = "Jet";
                    var tableName = reader.GetValueOrDefault<string>("Table");
                    var indexName = reader.GetValueOrDefault<string>("Name");
                    var isUnique = reader.GetValueOrDefault<bool>("IsUnique");
                    var typeDesc = "NON CLUSTERED";
                    var columnName = reader.GetValueOrDefault<string>("ColumnName");
                    var indexOrdinal = reader.GetValueOrDefault<int>("Ordinal");

                    Logger.IndexColumnFound(
                        DisplayName(schemaName, tableName), indexName, isUnique, columnName, indexOrdinal);

                    Debug.Assert(index == null || index.Table != null);
                    if (index == null
                        || index.Name != indexName
                        // ReSharper disable once PossibleNullReferenceException
                        || index.Table.Name != tableName
                        || index.Table.Schema != schemaName)
                    {
                        if (!_tables.TryGetValue(SchemaQualifiedKey(tableName, schemaName), out var table))
                        {
                            Logger.IndexTableMissingWarning(indexName, DisplayName(schemaName, tableName));
                            continue;
                        }

                        index = new DatabaseIndex
                        {
                            Table = table,
                            Name = indexName,
                            IsUnique = isUnique,
                            Filter = null
                        };

                        if (typeDesc == "CLUSTERED")
                        {
                            index[JetAnnotationNames.Clustered] = true;
                        }

                        table.Indexes.Add(index);
                    }

                    if (_tableColumns.TryGetValue(ColumnKey(index.Table, columnName), out var column))
                    {
                        index.Columns.Add(column);
                    }
                }
            }
        }

        private void GetForeignKeys()
        {
            var command = _connection.CreateCommand();
            command.CommandText = @"
SELECT 
    [#ForeignKeys].ToTable,
    [#ForeignKeyConstraints].Id AS NAME,
    [#ForeignKeys].FromTable,
    [#TableColumns].NAME AS FromColumnName,
    [#TableColumns_1].NAME AS ToColumnName,
    [#ForeignKeyConstraints].UpdateRule,
    [#ForeignKeyConstraints].DeleteRule,
    [#ForeignKeys].Ordinal
FROM ((
    (SHOW ForeignKeys) 
INNER JOIN 
    (SHOW ForeignKeyConstraints) 
            ON [#ForeignKeys].ConstraintId = [#ForeignKeyConstraints].Id) 
INNER JOIN 
    (SHOW TableColumns) 
        ON [#ForeignKeys].FromColumnId = [#TableColumns].Id
    )
INNER JOIN 
    (SHOW TableColumns) AS [#TableColumns_1]
        ON [#ForeignKeys].ToColumnId = [#TableColumns_1].Id
ORDER BY 
    [#ForeignKeys].ToTable, 
    [#ForeignKeyConstraints].Id, 
    [#ForeignKeys].Ordinal
; ";

            using (var reader = command.ExecuteReader())
            {
                var lastFkName = string.Empty;
                var lastFkSchemaName = string.Empty;
                var lastFkTableName = string.Empty;
                DatabaseForeignKey foreignKey = null;
                while (reader.Read())
                {
                    var schemaName = "Jet";
                    var tableName = reader.GetValueOrDefault<string>("FromTable");
                    var constraintName = reader.GetValueOrDefault<string>("Name");
                    var principalTableSchemaName = "Jet";
                    var principalTableName = reader.GetValueOrDefault<string>("ToTable");
                    var fromColumnName = reader.GetValueOrDefault<string>("FromColumnName");
                    var toColumnName = reader.GetValueOrDefault<string>("ToColumnName");
                    var updateAction = reader.GetValueOrDefault<string>("UpdateRule");
                    var deleteAction = reader.GetValueOrDefault<string>("DeleteRule");
                    var ordinal = reader.GetValueOrDefault<int>("Ordinal");

                    Logger.ForeignKeyColumnFound(
                        DisplayName(schemaName, tableName), constraintName, DisplayName(principalTableSchemaName, principalTableName),
                        fromColumnName, toColumnName, updateAction, deleteAction, ordinal);

                    if (foreignKey == null
                        || lastFkSchemaName != schemaName
                        || lastFkTableName != tableName
                        || lastFkName != constraintName)
                    {
                        lastFkName = constraintName;
                        lastFkSchemaName = schemaName;
                        lastFkTableName = tableName;

                        if (!_tables.TryGetValue(SchemaQualifiedKey(tableName, schemaName), out var table))
                        {
                            Logger.ForeignKeyTableMissingWarning(constraintName, DisplayName(schemaName, tableName));
                            continue;
                        }

                        DatabaseTable principalTable = null;
                        if (!string.IsNullOrEmpty(principalTableSchemaName)
                            && !string.IsNullOrEmpty(principalTableName))
                        {
                            _tables.TryGetValue(SchemaQualifiedKey(principalTableName, principalTableSchemaName), out principalTable);
                        }

                        if (principalTable == null)
                        {
                            Logger.ForeignKeyReferencesMissingPrincipalTableWarning(
                                constraintName, DisplayName(schemaName, tableName), DisplayName(principalTableSchemaName, principalTableName));
                        }

                        foreignKey = new DatabaseForeignKey
                        {
                            Name = constraintName,
                            Table = table,
                            PrincipalTable = principalTable,
                            OnDelete = ConvertToReferentialAction(deleteAction)
                        };

                        table.ForeignKeys.Add(foreignKey);
                    }

                    if (_tableColumns.TryGetValue(
                        ColumnKey(foreignKey.Table, fromColumnName), out var fromColumn))
                    {
                        foreignKey.Columns.Add(fromColumn);
                    }

                    if (foreignKey.PrincipalTable != null)
                    {
                        if (_tableColumns.TryGetValue(
                            ColumnKey(foreignKey.PrincipalTable, toColumnName), out var toColumn))
                        {
                            foreignKey.PrincipalColumns.Add(toColumn);
                        }
                    }
                }
            }
        }

        private static string DisplayName(string schema, string name)
            => (!string.IsNullOrEmpty(schema) ? schema + "." : "") + name;

        private static ReferentialAction? ConvertToReferentialAction(string onDeleteAction)
        {
            switch (onDeleteAction.ToUpperInvariant())
            {
                case "RESTRICT":
                    return ReferentialAction.Restrict;

                case "CASCADE":
                    return ReferentialAction.Cascade;

                case "SET NULL":
                    return ReferentialAction.SetNull;

                case "SET DEFAULT":
                    return ReferentialAction.SetDefault;

                case "NO ACTION":
                    return ReferentialAction.NoAction;

                default:
                    return null;
            }
        }
    }
}
