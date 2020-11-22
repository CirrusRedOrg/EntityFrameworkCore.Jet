// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using EntityFrameworkCore.Jet.Data;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
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
            @"(?<=^`).*(?=`$)",
            @"(?<=^\[).*(?=\]$)$",
        };
        
        private static readonly Regex _defaultDateTimeValue = new Regex(@"\(*(?:#0?1/0?1/0?100#)|(?:#0?0:0?0:0?0#)|(?:(['""])(0?0:0?0:0?0)|0?100-0?1-0?1(?: \2)\1)\)*");

        private readonly IDiagnosticsLogger<DbLoggerCategory.Scaffolding> _logger;
        
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetDatabaseModelFactory([NotNull] IDiagnosticsLogger<DbLoggerCategory.Scaffolding> logger)
        {
            Check.NotNull(logger, nameof(logger));

            _logger = logger;
        }

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
                var tableFilter = GenerateTableFilter(tableList.Select(Parse).ToList());
                
                foreach (var table in GetTables(connection, tableFilter))
                {
                    table.Database = databaseModel;
                    databaseModel.Tables.Add(table);
                }
                
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

        private static string Parse(string tableName)
        {
            foreach (var tablePattern in _tablePatterns)
            {
                var match = Regex.Match(tableName, tablePattern);
                if (match.Success)
                {
                    return match.Value;
                }
            }

            return tableName;
        }

        private IReadOnlyList<DatabaseTable> GetTables(
            DbConnection connection,
            Func<string, string, bool> filter)
        {
            var tables = new List<DatabaseTable>();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = $@"SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE TABLE_NAME <> '{HistoryRepository.DefaultTableName}' AND TABLE_TYPE IN ('BASE TABLE', 'VIEW')";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var name = reader.GetValueOrDefault<string>("TABLE_NAME");
                    var type = reader.GetValueOrDefault<string>("TABLE_TYPE");
                    var validationRule = reader.GetValueOrDefault<string>("VALIDATION_RULE");
                    var validationText = reader.GetValueOrDefault<string>("VALIDATION_TEXT");
                    
                    _logger.TableFound(name);

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

            if (tables.Count > 0)
            {
                GetColumns(connection, tables);
                GetIndexes(connection, tables);
                GetRelations(connection, tables);
            }

            return tables;
        }
        
        private void GetColumns(DbConnection connection, IReadOnlyList<DatabaseTable> tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $@"SELECT * FROM `INFORMATION_SCHEMA.COLUMNS` ORDER BY TABLE_NAME, ORDINAL_POSITION";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var tableName = reader.GetValueOrDefault<string>("TABLE_NAME");
                var table = tables.FirstOrDefault(t => string.Equals(t.Name, tableName)) ??
                            tables.FirstOrDefault(t => string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase));
                if (table != null)
                {
                    var columnName = reader.GetValueOrDefault<string>("COLUMN_NAME");
                    var dataTypeName = reader.GetValueOrDefault<string>("DATA_TYPE");
                    var ordinal = reader.GetValueOrDefault<int>("ORDINAL_POSITION");
                    var nullable = reader.GetValueOrDefault<bool>("IS_NULLABLE");
                    var maxLength = reader.GetValueOrDefault<int>("CHARACTER_MAXIMUM_LENGTH");
                    var precision = reader.GetValueOrDefault<int>("NUMERIC_PRECISION");
                    var scale = reader.GetValueOrDefault<int>("NUMERIC_SCALE");
                    var defaultValue = reader.GetValueOrDefault<string>("COLUMN_DEFAULT");
                    var validationRule = reader.GetValueOrDefault<string>("VALIDATION_RULE");
                    var validationText = reader.GetValueOrDefault<string>("VALIDATION_TEXT");
                    var identitySeed = reader.GetValueOrDefault<int?>("IDENTITY_SEED");
                    var identityIncrement = reader.GetValueOrDefault<int?>("IDENTITY_INCREMENT");
                    var computedValue = (string) null; // TODO: Implement support for expressions
                                                       // (DAO Field2 (though not mentioned)).
                                                       // Might have no equivalent in ADOX.
                    var computedIsPersisted = false;

                    _logger.ColumnFound(
                        tableName,
                        columnName,
                        ordinal,
                        dataTypeName,
                        maxLength,
                        precision,
                        scale,
                        nullable,
                        identitySeed.HasValue,
                        defaultValue,
                        computedValue,
                        computedIsPersisted);
                    
                    var storeType = GetStoreType(dataTypeName, precision, scale, maxLength);
                    defaultValue = FilterClrDefaults(dataTypeName, nullable, defaultValue);
                    
                    var column = new DatabaseColumn
                    {
                        Table = table,
                        Name = columnName,
                        StoreType = storeType,
                        IsNullable = nullable,
                        DefaultValueSql = defaultValue,
                        ComputedColumnSql = null,
                        ValueGenerated = identitySeed.HasValue
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
        
        private static string FilterClrDefaults(string dataTypeName, bool nullable, string defaultValue)
        {
            if (defaultValue == null)
            {
                return null;
            }

            if (nullable)
            {
                return defaultValue;
            }

            if (defaultValue == "0")
            {
                if (dataTypeName == "smallint"
                    || dataTypeName == "integer"
                    || dataTypeName == "single"
                    || dataTypeName == "double"
                    || dataTypeName == "currency"
                    || dataTypeName == "bit"
                    || dataTypeName == "decimal"
                    || dataTypeName == "byte")
                {
                    return null;
                }
            }
            else if (defaultValue == "0.0")
            {
                if (dataTypeName == "decimal"
                    || dataTypeName == "double"
                    || dataTypeName == "single"
                    || dataTypeName == "currency")
                {
                    return null;
                }
            }
            else if ((dataTypeName == "datetime" && _defaultDateTimeValue.IsMatch(defaultValue)) ||
                     (dataTypeName == "guid" && defaultValue == "'00000000-0000-0000-0000-000000000000'"))
            {
                return null;
            }

            return defaultValue;
        }

        private string GetStoreType(string dataTypeName, int precision, int scale, int maxLength)
        {
            if (precision > 0 &&
                string.Equals(dataTypeName, "decimal", StringComparison.OrdinalIgnoreCase))
            {
                return $"{dataTypeName}({precision}, {scale})";
            }

            if (maxLength > 0)
            {
                return $"{dataTypeName}({maxLength})";
            }

            if (string.Equals(dataTypeName, "varchar", StringComparison.OrdinalIgnoreCase))
            {
                return "longchar";
            }

            if (string.Equals(dataTypeName, "varbinary", StringComparison.OrdinalIgnoreCase))
            {
                return "longbinary";
            }

            return dataTypeName;
        }

        private void GetIndexes(DbConnection connection, IReadOnlyList<DatabaseTable> tables)
        {
            var indexTable = new DataTable();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $@"SELECT * FROM `INFORMATION_SCHEMA.INDEXES` ORDER BY TABLE_NAME, INDEX_NAME";

                using var reader = command.ExecuteReader();
                indexTable.Load(reader);
            }

            var indexColumnsTable = new DataTable();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM `INFORMATION_SCHEMA.INDEX_COLUMNS` ORDER BY TABLE_NAME, INDEX_NAME, ORDINAL_POSITION";
                using var reader = command.ExecuteReader();
                indexColumnsTable.Load(reader);
            }

            var groupedIndexColumns = indexColumnsTable.Rows.Cast<DataRow>()
                .GroupBy(r => (TableName: r.GetValueOrDefault<string>("TABLE_NAME"), IndexName: r.GetValueOrDefault<string>("INDEX_NAME")))
                .ToList();

            foreach (DataRow indexRow in indexTable.Rows)
            {
                var tableName = indexRow.GetValueOrDefault<string>("TABLE_NAME");
                var indexName = indexRow.GetValueOrDefault<string>("INDEX_NAME");
                var indexType = indexRow.GetValueOrDefault<string>("INDEX_TYPE");
                var nullable = indexRow.GetValueOrDefault<bool>("IS_NULLABLE");
                var ignoresNulls = indexRow.GetValueOrDefault<bool>("IGNORES_NULLS");

                var table = tables.FirstOrDefault(t => string.Equals(t.Name, tableName)) ??
                            tables.FirstOrDefault(t => string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase));
                if (table != null)
                {
                    var indexColumns = groupedIndexColumns.FirstOrDefault(g => g.Key == (tableName, indexName));
                    if (indexColumns?.Any() ?? false)
                    {
                        object indexOrKey = null;
                        
                        if (indexType == "PRIMARY")
                        {
                            var primaryKey = new DatabasePrimaryKey
                            {
                                Table = table,
                                Name = indexName,
                            };

                            _logger.PrimaryKeyFound(indexName, tableName);

                            table.PrimaryKey = primaryKey;
                            indexOrKey = primaryKey;
                        }
                        else if (indexType == "UNIQUE" &&
                                 !nullable)
                        {
                            var uniqueConstraint = new DatabaseUniqueConstraint
                            {
                                Table = table,
                                Name = indexName,
                            };

                            _logger.UniqueConstraintFound(indexName, tableName);

                            table.UniqueConstraints.Add(uniqueConstraint);
                            indexOrKey = uniqueConstraint;
                        }
                        else
                        {
                            var index = new DatabaseIndex
                            {
                                Table = table,
                                Name = indexName,
                                IsUnique = indexType == "UNIQUE",
                            };

                            _logger.IndexFound(indexName, tableName, index.IsUnique);

                            table.Indexes.Add(index);
                            indexOrKey = index;
                        }
                        
                        foreach (var indexColumn in indexColumns)
                        {
                            var columnName = indexColumn.GetValueOrDefault<string>("COLUMN_NAME");
                            var descending = indexColumn.GetValueOrDefault<bool>("IS_DESCENDING");

                            var column = table.Columns.FirstOrDefault(c => c.Name == columnName) ??
                                         table.Columns.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
                            if (column != null)
                            {
                                switch (indexOrKey)
                                {
                                    case DatabasePrimaryKey primaryKey:
                                        primaryKey.Columns.Add(column);
                                        break;
                                    
                                    case DatabaseUniqueConstraint uniqueConstraint:
                                        uniqueConstraint.Columns.Add(column);
                                        break;
                                    
                                    case DatabaseIndex index:
                                        index.Columns.Add(column);
                                        break;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        private void GetRelations(DbConnection connection, IReadOnlyList<DatabaseTable> tables)
        {
            var relationTable = new DataTable();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $@"SELECT * FROM `INFORMATION_SCHEMA.RELATIONS` ORDER BY RELATION_NAME, REFERENCING_TABLE_NAME, PRINCIPAL_TABLE_NAME";

                using var reader = command.ExecuteReader();
                relationTable.Load(reader);
            }

            var relationColumnsTable = new DataTable();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM `INFORMATION_SCHEMA.RELATION_COLUMNS` ORDER BY RELATION_NAME, ORDINAL_POSITION"; // no sorting would be fine as well
                using var reader = command.ExecuteReader();
                relationColumnsTable.Load(reader);
            }

            var groupedRelationColumns = relationColumnsTable.Rows.Cast<DataRow>()
                .GroupBy(r => r.GetValueOrDefault<string>("RELATION_NAME"))
                .ToList();

            foreach (DataRow relationRow in relationTable.Rows)
            {
                var relationName = relationRow.GetValueOrDefault<string>("RELATION_NAME");
                var referencingTableName = relationRow.GetValueOrDefault<string>("REFERENCING_TABLE_NAME");
                var principalTableName = relationRow.GetValueOrDefault<string>("PRINCIPAL_TABLE_NAME");
                var relationType = relationRow.GetValueOrDefault<string>("RELATION_TYPE");
                var onDelete = relationRow.GetValueOrDefault<string>("ON_DELETE");
                var onUpdate = relationRow.GetValueOrDefault<string>("ON_UPDATE");
                var enforced = relationRow.GetValueOrDefault<bool>("IS_ENFORCED", true);
                var inherited = relationRow.GetValueOrDefault<bool>("IS_INHERITED", true);

                var referencingTable = tables.FirstOrDefault(t => string.Equals(t.Name, referencingTableName)) ??
                            tables.FirstOrDefault(t => string.Equals(t.Name, referencingTableName, StringComparison.OrdinalIgnoreCase));
                if (referencingTable != null)
                {
                    var relationColumns = groupedRelationColumns.FirstOrDefault(g => g.Key == relationName);
                    if (relationColumns?.Any() ?? false)
                    {
                        _logger.ForeignKeyFound(
                            relationName,
                            referencingTableName,
                            principalTableName,
                            onDelete);
                        
                        var principalTable = tables.FirstOrDefault(t => string.Equals(t.Name, principalTableName)) ??
                                             tables.FirstOrDefault(t => string.Equals(t.Name, principalTableName, StringComparison.OrdinalIgnoreCase));
                        if (principalTable == null)
                        {
                            _logger.ForeignKeyReferencesMissingPrincipalTableWarning(
                                relationName,
                                referencingTableName,
                                principalTableName);
                            continue;
                        }
                        
                        var foreignKey = new DatabaseForeignKey
                        {
                            Name = relationName,
                            Table = referencingTable,
                            PrincipalTable = principalTable,
                            OnDelete = ConvertToReferentialAction(onDelete),
                        };

                        var invalid = false;
                        foreach (var relationColumn in relationColumns)
                        {
                            var referencingColumnName = relationColumn.GetValueOrDefault<string>("REFERENCING_COLUMN_NAME");
                            var referencingColumn = referencingTable.Columns.FirstOrDefault(c => c.Name == referencingColumnName) ??
                                                    referencingTable.Columns.FirstOrDefault(c => string.Equals(c.Name, referencingColumnName, StringComparison.OrdinalIgnoreCase));
                            Debug.Assert(referencingColumn != null, "referencingColumn is null.");

                            var principalColumnName = relationColumn.GetValueOrDefault<string>("PRINCIPAL_COLUMN_NAME");
                            var principalColumn = principalTable.Columns.FirstOrDefault(c => c.Name == principalColumnName) ??
                                                  principalTable.Columns.FirstOrDefault(c => string.Equals(c.Name, principalColumnName, StringComparison.OrdinalIgnoreCase));
                            if (principalColumn == null)
                            {
                                invalid = true;
                                _logger.ForeignKeyPrincipalColumnMissingWarning(
                                    relationName,
                                    referencingTableName,
                                    principalColumnName,
                                    principalTableName);
                                break;
                            }

                            foreignKey.Columns.Add(referencingColumn);
                            foreignKey.PrincipalColumns.Add(principalColumn);
                        }

                        if (invalid)
                        {
                            continue;
                        }
                        
                        if (foreignKey.Columns.SequenceEqual(foreignKey.PrincipalColumns))
                        {
                            _logger.ReflexiveConstraintIgnored(
                                foreignKey.Name,
                                referencingTableName);
                        }
                        else
                        {
                            referencingTable.ForeignKeys.Add(foreignKey);
                        }
                    }
                }
            }
        }

        private static ReferentialAction? ConvertToReferentialAction(string onDeleteAction)
        {
            switch (onDeleteAction.ToUpperInvariant())
            {
                case "RESTRICT": // TODO: does not exist in Jet
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