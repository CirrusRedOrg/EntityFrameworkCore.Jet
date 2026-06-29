using System.Data.Common;
using EntityFrameworkCore.Jet.Internal; // FK scaffolding-logger extensions (ForeignKeyFound, …)
using LibRed.Catalog;
using LibRed.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;

namespace LibRed.EntityFrameworkCore.Scaffolding;

/// <summary>
/// Builds the scaffolding <see cref="DatabaseModel"/> directly from LibRed's own catalog
/// (<c>JetCatalog</c>) — tables, columns, primary keys, indexes and relationships — replacing
/// EFCore.Jet's INFORMATION_SCHEMA-over-ADOX reader. Cross-platform, no Access required. The
/// shape mirrors <c>JetDatabaseModelFactory</c> (Get* helpers + scaffolding logging) so the two
/// stay easy to compare.
/// </summary>
public class LibRedDatabaseModelFactory(IDiagnosticsLogger<DbLoggerCategory.Scaffolding> logger)
    : DatabaseModelFactory
{
    private readonly IDiagnosticsLogger<DbLoggerCategory.Scaffolding> _logger = logger;

    public override DatabaseModel Create(string connectionString, DatabaseModelFactoryOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentNullException.ThrowIfNull(options);

        using var connection = new LibRedConnection(connectionString);
        return Create(connection, options);
    }

    public override DatabaseModel Create(DbConnection connection, DatabaseModelFactoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);

        string path = connection is LibRedConnection libred ? libred.DataSource : new LibRedConnection(connection.ConnectionString).DataSource;
        using var database = JetDatabase.Open(path);

        var databaseModel = new DatabaseModel { DatabaseName = Path.GetFileNameWithoutExtension(path) };

        var tableList = options.Tables.ToList();
        Func<string, bool>? tableFilter = GenerateTableFilter(tableList);

        foreach (DatabaseTable table in GetTables(database, databaseModel, tableFilter))
        {
            databaseModel.Tables.Add(table);
        }

        foreach (string table in tableList)
        {
            if (databaseModel.Tables.All(t => !string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.MissingTableWarning(table);
            }
        }

        return databaseModel;
    }

    private IReadOnlyList<DatabaseTable> GetTables(JetDatabase database, DatabaseModel databaseModel, Func<string, bool>? filter)
    {
        var tables = new List<DatabaseTable>();

        foreach (TableDef definition in database.Catalog.UserTables)
        {
            _logger.TableFound(definition.Name);

            if (filter is not null && !filter(definition.Name)) continue;

            tables.Add(new DatabaseTable { Database = databaseModel, Name = definition.Name });
        }

        if (tables.Count > 0)
        {
            GetColumns(database, tables);
            GetIndexes(database, tables);
            GetRelations(database, tables);
        }

        return tables;
    }

    private void GetColumns(JetDatabase database, IReadOnlyList<DatabaseTable> tables)
    {
        foreach (DatabaseTable table in tables)
        {
            TableDef definition = database.Catalog.FindTable(table.Name)!;

            for (int ordinal = 0; ordinal < definition.Columns.Count; ordinal++)
            {
                ColumnDef column = definition.Columns[ordinal];
                string storeType = GetStoreType(column);
                int maxLength = column.Type == JetDataType.Text ? Math.Max(1, column.Length / 2) : 0;
                // Nullability is not yet readable from the format (see jet-ace-file-format.md §3.4);
                // auto-number columns are never nullable, everything else is reported nullable.
                bool nullable = storeType != "counter";

                _logger.ColumnFound(
                    table.Name,
                    column.Name,
                    ordinal + 1,
                    storeType,
                    maxLength,
                    column.Precision,
                    column.Scale,
                    nullable,
                    column.IsAutoNumber,
                    null,  // defaultValue
                    null,  // computedValue
                    null); // computed-is-stored

                table.Columns.Add(new DatabaseColumn
                {
                    Table = table,
                    Name = column.Name,
                    StoreType = storeType,
                    IsNullable = nullable,
                    ValueGenerated = column.IsAutoNumber ? ValueGenerated.OnAdd : null,
                });
            }
        }
    }

    private void GetIndexes(JetDatabase database, IReadOnlyList<DatabaseTable> tables)
    {
        foreach (DatabaseTable table in tables)
        {
            TableDef definition = database.Catalog.FindTable(table.Name)!;

            foreach (IndexDef index in definition.Indexes)
            {
                List<DatabaseColumn> columns = index.Columns
                    .Select(c => table.Columns.First(dc => dc.Name == c.Column.Name))
                    .ToList();

                if (index.IsPrimaryKey)
                {
                    _logger.PrimaryKeyFound(index.Name, table.Name);
                    var pk = new DatabasePrimaryKey { Table = table, Name = index.Name };
                    foreach (DatabaseColumn c in columns) pk.Columns.Add(c);
                    table.PrimaryKey = pk;
                }
                else if (index.IsUnique)
                {
                    _logger.UniqueConstraintFound(index.Name, table.Name);
                    var unique = new DatabaseUniqueConstraint { Table = table, Name = index.Name };
                    foreach (DatabaseColumn c in columns) unique.Columns.Add(c);
                    table.UniqueConstraints.Add(unique);
                }
                else
                {
                    _logger.IndexFound(index.Name, table.Name, index.IsUnique);
                    var dbIndex = new DatabaseIndex { Table = table, Name = index.Name, IsUnique = index.IsUnique };
                    foreach (DatabaseColumn c in columns) dbIndex.Columns.Add(c);
                    table.Indexes.Add(dbIndex);
                }
            }
        }
    }

    private void GetRelations(JetDatabase database, IReadOnlyList<DatabaseTable> tables)
    {
        DatabaseTable? Find(string name)
            => tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        foreach (ForeignKey relation in database.Catalog.Relationships)
        {
            DatabaseTable? referencingTable = Find(relation.Table);
            if (referencingTable is null) continue;

            string onDelete = relation.CascadeDelete ? "CASCADE" : "NO ACTION";
            _logger.ForeignKeyFound(relation.Name, relation.Table, relation.ReferencedTable, onDelete);

            DatabaseTable? principalTable = Find(relation.ReferencedTable);
            if (principalTable is null)
            {
                _logger.ForeignKeyReferencesMissingPrincipalTableWarning(relation.Name, relation.Table, relation.ReferencedTable);
                continue;
            }

            var foreignKey = new DatabaseForeignKey
            {
                Name = relation.Name,
                Table = referencingTable,
                PrincipalTable = principalTable,
                OnDelete = relation.CascadeDelete ? ReferentialAction.Cascade : ReferentialAction.NoAction,
            };

            bool invalid = false;
            foreach (var (column, referenced) in relation.Columns)
            {
                DatabaseColumn? referencingColumn = referencingTable.Columns.FirstOrDefault(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));
                DatabaseColumn? principalColumn = principalTable.Columns.FirstOrDefault(c => string.Equals(c.Name, referenced, StringComparison.OrdinalIgnoreCase));

                if (principalColumn is null || referencingColumn is null)
                {
                    invalid = true;
                    _logger.ForeignKeyPrincipalColumnMissingWarning(relation.Name, relation.Table, referenced, relation.ReferencedTable);
                    break;
                }

                foreignKey.Columns.Add(referencingColumn);
                foreignKey.PrincipalColumns.Add(principalColumn);
            }

            if (invalid) continue;

            if (foreignKey.Columns.SequenceEqual(foreignKey.PrincipalColumns))
            {
                _logger.ReflexiveConstraintIgnored(foreignKey.Name!, relation.Table);
                continue;
            }

            referencingTable.ForeignKeys.Add(foreignKey);
        }
    }

    private static Func<string, bool>? GenerateTableFilter(IReadOnlyList<string> tables)
    {
        var wanted = tables.Where(t => !string.IsNullOrWhiteSpace(t)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return wanted.Count > 0 ? name => wanted.Contains(name) : null;
    }

    /// <summary>Maps a column to a store-type name EFCore.Jet's type mapping source recognises.</summary>
    private static string GetStoreType(ColumnDef column) => column.Type switch
    {
        JetDataType.Boolean => "bit",
        JetDataType.Byte => "byte",
        JetDataType.Int16 => "smallint",
        JetDataType.Int32 => column.IsAutoNumber ? "counter" : "integer",
        JetDataType.Int64 => "bigint",
        JetDataType.Single => "single",
        JetDataType.Double => "double",
        JetDataType.Currency => "currency",
        JetDataType.DateTime or JetDataType.DateTimeExtended => "datetime",
        JetDataType.Guid => "guid",
        JetDataType.FixedPoint => $"decimal({column.Precision}, {column.Scale})",
        JetDataType.Text => $"varchar({Math.Max(1, column.Length / 2)})",
        JetDataType.Binary => $"varbinary({Math.Max(1, column.Length)})",
        JetDataType.Memo => "longchar",
        JetDataType.Ole => "longbinary",
        _ => "varchar(255)",
    };
}
