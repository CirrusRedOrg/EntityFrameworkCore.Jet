// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Jet;
using System.Diagnostics;
using System.Linq;
using EntityFrameworkCore.Jet.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using EntityFrameworkCore.Jet.Utilities;

namespace EntityFrameworkCore.Jet.Scaffolding.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetDatabaseModelFactory : DatabaseModelFactory
    {
        private static string ObjectKey([NotNull] string name)
            => "`" + name + "`";
        
        private static string TableKey(DatabaseTable table)
            => TableKey(table.Name);
        
        private static string TableKey(String tableName)
            => ObjectKey(tableName);
        
        private static string ColumnKey(DatabaseTable table, string columnName)
            => TableKey(table) + "." + ObjectKey(columnName);

        private static readonly List<string> _tablePatterns = new List<string>
        {
            "{table}",
            "`{table}`",
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
        
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override DatabaseModel Create(string connectionString, DatabaseModelFactoryOptions options)
        {
            Check.NotEmpty(connectionString, nameof(connectionString));
            Check.NotNull(options, nameof(options));

            using var connection = new JetConnection(connectionString);
            return Create(connection, options);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override DatabaseModel Create(
            DbConnection connection,
            DatabaseModelFactoryOptions options)
        {
            Check.NotNull(connection, nameof(connection));
            Check.NotNull(options, nameof(options));

            var databaseModel = new DatabaseModel();

            var connectionStartedOpen = connection.State == ConnectionState.Open;
            if (!connectionStartedOpen)
            {
                connection.Open();
            }

            try
            {
                var tableList = options.Tables.ToList();
                var tableFilter = GenerateTableFilter(tableList);
                
                foreach (var table in GetTables(connection, tableFilter))
                {
                    table.Database = databaseModel;
                    databaseModel.Tables.Add(table);
                }
                
                // GetTables();
                // GetColumns();
                // GetPrimaryKeys();
                // GetUniqueConstraints();
                // GetIndexes();
                // GetForeignKeys();

                return databaseModel;
            }
            finally
            {
                if (!connectionStartedOpen)
                {
                    connection.Close();
                }
            }
        }
        
        private IReadOnlyList<DatabaseTable> GetTables(
            DbConnection connection,
            Func<string, string, bool> filter)
        {
            var tables = new List<DatabaseTable>();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE Name <> '{HistoryRepository.DefaultTableName}' AND TABLE_TYPE <> 'SYSTEM TABLE'";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var name = reader.GetValueOrDefault<string>("TABLE_NAME");
                    var type = reader.GetValueOrDefault<string>("TABLE_TYPE");

                    var table = string.Equals(type, "BASE TABLE", StringComparison.OrdinalIgnoreCase)
                        ? new DatabaseTable()
                        : new DatabaseView();

                    table.Name = name;

                    var isValidByFilter = filter?.Invoke(table.Schema, table.Name) ?? true;
                    if (isValidByFilter)
                    {
                        tables.Add(table);
                    }
                }
            }
            
            GetColumns(connection, tables);
            //GetPrimaryKeys(connection, tables);
            //GetIndexes(connection, tables, filter);
            //GetForeignKeys();

            return tables;
        }
        
        private void GetColumns(DbConnection connection, IReadOnlyList<DatabaseTable> tables)
        {
            foreach (var table in tables)
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $@"SELECT * FROM `INFORMATION_SCHEMA.COLUMNS` WHERE TABLE_NAME = '{table.Name}' ORDER BY ORDINAL_POSITION";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var columnName = reader.GetValueOrDefault<string>("Name");
                            var dataTypeName = reader.GetValueOrDefault<string>("TypeName");
                            var nullable = reader.GetValueOrDefault<bool>("IsNullable");
                            // ReSharper disable once UnusedVariable
                            var primaryKeyOrdinal = reader.GetValueOrDefault<bool>("IsKey")
                                ? (int?) reader.GetValueOrDefault<int>("Ordinal")
                                : null;
                            var defaultValue = reader.GetValueOrDefault<string>("Default");
                            var precision = reader.GetValueOrDefault<int?>("Precision");
                            var scale = reader.GetValueOrDefault<int?>("Scale");
                            var maxLength = reader.GetValueOrDefault<int?>("MaxLength");
                            var isIdentity = reader.GetValueOrDefault<bool>("IsIdentity");
                            
                            var storeType = GetStoreType(dataTypeName, precision, scale, maxLength);

                            // CHECK: Jet behavior.
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
                                ComputedColumnSql = null,
                                ValueGenerated = isIdentity
                                    ? ValueGenerated.OnAdd
                                    : storeType == "timestamp"
                                        ? ValueGenerated.OnAddOrUpdate
                                        : default(ValueGenerated?)
                            };

                            if (storeType == "timestamp")
                            {
                                // Note: annotation name must match `ScaffoldingAnnotationNames.ConcurrencyToken`
                                column["ConcurrencyToken"] = true;
                            }

                            table.Columns.Add(column);
                        }
                    }
                }
            }
        }

        private string GetStoreType(string dataTypeName, int? precision, int? scale, int? maxLength)
        {
            if (string.Equals(dataTypeName, "decimal", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dataTypeName, "numeric", StringComparison.OrdinalIgnoreCase))
            {
                return $"{dataTypeName}({precision}, {scale.GetValueOrDefault(0)})";
            }

            if (maxLength.HasValue)
            {
                if (maxLength == -1)
                {
                    if (string.Equals(dataTypeName, "varchar", StringComparison.OrdinalIgnoreCase))
                        return "longchar";

                    if (string.Equals(dataTypeName, "varbinary", StringComparison.OrdinalIgnoreCase))
                        return "longbinary";

                    throw new InvalidOperationException("Unexpected type " + dataTypeName);
                }

                if (maxLength > 0)
                    return $"{dataTypeName}({maxLength.Value})";
            }

            return dataTypeName;
        }

        /*
        private void GetPrimaryKeys()
        {
            var command = _connection.CreateCommand();
            command.CommandText =
                "SHOW " +
                "   CONSTRAINTCOLUMNS " +
                "WHERE " +
                "   ConstraintType = 'PRIMARY KEY' AND TableName <> '" + HistoryRepository.DefaultTableName + "' " +
                "ORDER BY " +
                "   TableName, ConstraintName, ColumnOrdinal";

            using (var reader = command.ExecuteReader())
            {
                DatabasePrimaryKey primaryKey = null;
                while (reader.Read())
                {
                    var tableName = reader.GetValueOrDefault<string>("TableName");
                    var indexName = reader.GetValueOrDefault<string>("ConstraintName");
                    var columnName = reader.GetValueOrDefault<string>("ColumnName");
                    // ReSharper disable once UnusedVariable
                    var indexOrdinal = reader.GetValueOrDefault<int>("ColumnOrdinal");

                    Debug.Assert(primaryKey == null || primaryKey.Table != null);
                    if (primaryKey == null
                        || primaryKey.Name != indexName
                        // ReSharper disable once PossibleNullReferenceException
                        || primaryKey.Table.Name != tableName)
                    {
                        if (!_tables.TryGetValue(TableKey(tableName), out var table))
                        {
                            continue;
                        }

                        Logger.PrimaryKeyFound(indexName, tableName);

                        primaryKey = new DatabasePrimaryKey
                        {
                            Table = table,
                            Name = indexName
                        };

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
        */

        /*
        private void GetUniqueConstraints()
        {
            var command = _connection.CreateCommand();
            command.CommandText =
                "SHOW " +
                "   CONSTRAINTCOLUMNS " +
                "WHERE " +
                "   ConstraintType = 'UNIQUE' AND TableName <> '" + HistoryRepository.DefaultTableName + "' " +
                "ORDER BY " +
                "   TableName, ConstraintName, ColumnOrdinal";

            using (var reader = command.ExecuteReader())
            {
                DatabaseUniqueConstraint uniqueConstraint = null;
                while (reader.Read())
                {
                    var schemaName = "Jet"; // TODO: Change to `null`.
                    var tableName = reader.GetValueOrDefault<string>("TableName");
                    var indexName = reader.GetValueOrDefault<string>("ConstraintName");
                    var columnName = reader.GetValueOrDefault<string>("ColumnName");
                    // ReSharper disable once UnusedVariable
                    var indexOrdinal = reader.GetValueOrDefault<int>("ColumnOrdinal");

                    Debug.Assert(uniqueConstraint == null || uniqueConstraint.Table != null);
                    if (uniqueConstraint == null
                        || uniqueConstraint.Name != indexName
                        // ReSharper disable once PossibleNullReferenceException
                        || uniqueConstraint.Table.Name != tableName
                        || uniqueConstraint.Table.Schema != schemaName)
                    {
                        if (!_tables.TryGetValue(TableKey(tableName), out var table))
                        {
                            continue;
                        }

                        Logger.UniqueConstraintFound(indexName, tableName);

                        uniqueConstraint = new DatabaseUniqueConstraint
                        {
                            Table = table,
                            Name = indexName
                        };

                        table.UniqueConstraints.Add(uniqueConstraint);
                    }

                    if (_tableColumns.TryGetValue(ColumnKey(uniqueConstraint.Table, columnName), out var column))
                    {
                        uniqueConstraint.Columns.Add(column);
                    }
                }
            }
        }
        */

        /*
        private void GetIndexes()
        {
            var command = _connection.CreateCommand();
            command.CommandText =
                "SHOW " +
                "   IndexColumns " +
                "WHERE " +
                "   IsPrimary = False AND " +
                "   IsUnique = False AND " +
                "   Table <> '" + HistoryRepository.DefaultTableName + "' " +
                "ORDER BY " +
                "   Table, Index, Ordinal";

            using (var reader = command.ExecuteReader())
            {
                DatabaseIndex index = null;
                while (reader.Read())
                {
                    var schemaName = "Jet"; // TODO: Change to `null`.
                    var tableName = reader.GetValueOrDefault<string>("Table");
                    var indexName = reader.GetValueOrDefault<string>("Index");
                    var isUnique = reader.GetValueOrDefault<bool>("IsUnique");
                    var columnName = reader.GetValueOrDefault<string>("Name");
                    // ReSharper disable once UnusedVariable
                    var indexOrdinal = reader.GetValueOrDefault<int>("Ordinal");

                    Debug.Assert(index == null || index.Table != null);
                    if (index == null
                        || index.Name != indexName
                        // ReSharper disable once PossibleNullReferenceException
                        || index.Table.Name != tableName
                        || index.Table.Schema != schemaName)
                    {
                        if (!_tables.TryGetValue(TableKey(tableName), out var table))
                        {
                            continue;
                        }

                        Logger.IndexFound(indexName, tableName, isUnique);

                        index = new DatabaseIndex
                        {
                            Table = table,
                            Name = indexName,
                            IsUnique = isUnique,
                            Filter = null
                        };

                        table.Indexes.Add(index);
                    }

                    if (_tableColumns.TryGetValue(ColumnKey(index.Table, columnName), out var column))
                    {
                        index.Columns.Add(column);
                    }
                }
            }
        }
        */

        /*
        private void GetForeignKeys()
        {
            var command = _connection.CreateCommand();
            command.CommandText =
                "SHOW " +
                "    ForeignKeys " +
                "ORDER BY " +
                "    ToTable, " +
                "    ConstraintId, " +
                "    Ordinal";

            using (var reader = command.ExecuteReader())
            {
                var lastFkName = string.Empty;
                var lastFkSchemaName = string.Empty;
                var lastFkTableName = string.Empty;
                DatabaseForeignKey foreignKey = null;
                while (reader.Read())
                {
                    var schemaName = "Jet"; // TODO: Change to `null`.
                    var tableName = reader.GetValueOrDefault<string>("FromTable");
                    var constraintName = reader.GetValueOrDefault<string>("ConstraintId");
                    var principalTableSchemaName = "Jet"; // TODO: Change to `null`.
                    var principalTableName = reader.GetValueOrDefault<string>("ToTable");
                    var fromColumnName = reader.GetValueOrDefault<string>("FromColumn");
                    var toColumnName = reader.GetValueOrDefault<string>("ToColumn");
                    // ReSharper disable once UnusedVariable
                    var updateAction = reader.GetValueOrDefault<string>("UpdateRule");
                    var deleteAction = reader.GetValueOrDefault<string>("DeleteRule");
                    // ReSharper disable once UnusedVariable
                    var ordinal = reader.GetValueOrDefault<int>("Ordinal");

                    Logger.ForeignKeyFound(
                        constraintName,
                        tableName,
                        principalTableName,
                        deleteAction);

                    if (foreignKey == null
                        || lastFkSchemaName != schemaName
                        || lastFkTableName != tableName
                        || lastFkName != constraintName)
                    {
                        lastFkName = constraintName;
                        lastFkSchemaName = schemaName;
                        lastFkTableName = tableName;

                        if (!_tables.TryGetValue(TableKey(tableName), out var table))
                        {
                            continue;
                        }

                        DatabaseTable principalTable = null;
                        if (!string.IsNullOrEmpty(principalTableSchemaName)
                            && !string.IsNullOrEmpty(principalTableName))
                        {
                            _tables.TryGetValue(TableKey(principalTableName), out principalTable);
                        }

                        if (principalTable == null)
                        {
                            Logger.ForeignKeyReferencesMissingPrincipalTableWarning(
                                constraintName, tableName, principalTableName);
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
        */

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
        
        protected virtual Func<string, string, bool> GenerateTableFilter(IReadOnlyList<string> tables)
            => tables.Count > 0 ? (s, t) => tables.Contains(t, StringComparer.OrdinalIgnoreCase) : (Func<string, string, bool>)null;
    }
}