using System.Data.Common;
using LibRed.Catalog;
using LibRed.Data;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;

namespace LibRed.EntityFrameworkCore.Scaffolding;

/// <summary>
/// Builds the scaffolding <see cref="DatabaseModel"/> directly from LibRed's own catalog
/// (<c>JetCatalog</c>) — tables, columns, primary keys, indexes and relationships — replacing
/// EFCore.Jet's INFORMATION_SCHEMA-over-ADOX reader. Cross-platform, no Access required.
/// </summary>
public sealed class LibRedDatabaseModelFactory : DatabaseModelFactory
{
    public override DatabaseModel Create(string connectionString, DatabaseModelFactoryOptions options)
    {
        string path = new LibRedConnection(connectionString).DataSource;
        using var database = JetDatabase.Open(path);
        return Build(database, options);
    }

    public override DatabaseModel Create(DbConnection connection, DatabaseModelFactoryOptions options)
        => Create(connection.ConnectionString, options);

    private static DatabaseModel Build(JetDatabase database, DatabaseModelFactoryOptions options)
    {
        var model = new DatabaseModel();
        var wanted = options.Tables?.Where(t => !string.IsNullOrWhiteSpace(t)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, DatabaseTable>(StringComparer.OrdinalIgnoreCase);

        foreach (TableDef table in database.Catalog.UserTables)
        {
            if (wanted is { Count: > 0 } && !wanted.Contains(table.Name)) continue;

            var dbTable = new DatabaseTable { Database = model, Name = table.Name };
            IndexDef? primaryKey = table.Indexes.FirstOrDefault(i => i.IsPrimaryKey);
            var pkColumnNames = primaryKey?.Columns.Select(c => c.Column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
                                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ColumnDef column in table.Columns)
            {
                dbTable.Columns.Add(new DatabaseColumn
                {
                    Table = dbTable,
                    Name = column.Name,
                    StoreType = StoreType(column),
                    // Nullability is not yet read from the format; treat key columns as required.
                    IsNullable = !pkColumnNames.Contains(column.Name),
                    ValueGenerated = column.IsAutoNumber ? ValueGenerated.OnAdd : null,
                });
            }

            if (primaryKey is not null)
            {
                var dbPk = new DatabasePrimaryKey { Table = dbTable, Name = primaryKey.Name };
                foreach (var (col, _) in primaryKey.Columns)
                    dbPk.Columns.Add(dbTable.Columns.First(c => c.Name == col.Name));
                dbTable.PrimaryKey = dbPk;
            }

            foreach (IndexDef index in table.Indexes.Where(i => !i.IsPrimaryKey))
            {
                var dbIndex = new DatabaseIndex { Table = dbTable, Name = index.Name, IsUnique = index.IsUnique };
                foreach (var (col, _) in index.Columns)
                    dbIndex.Columns.Add(dbTable.Columns.First(c => c.Name == col.Name));
                dbTable.Indexes.Add(dbIndex);
            }

            model.Tables.Add(dbTable);
            byName[table.Name] = dbTable;
        }

        foreach (ForeignKey fk in database.Catalog.Relationships)
        {
            if (!byName.TryGetValue(fk.Table, out DatabaseTable? child)) continue;
            if (!byName.TryGetValue(fk.ReferencedTable, out DatabaseTable? parent)) continue;

            var dbFk = new DatabaseForeignKey
            {
                Table = child,
                Name = fk.Name,
                PrincipalTable = parent,
                OnDelete = fk.CascadeDelete ? ReferentialAction.Cascade : ReferentialAction.NoAction,
            };
            foreach (var (column, referenced) in fk.Columns)
            {
                dbFk.Columns.Add(child.Columns.First(c => c.Name == column));
                dbFk.PrincipalColumns.Add(parent.Columns.First(c => c.Name == referenced));
            }
            child.ForeignKeys.Add(dbFk);
        }

        return model;
    }

    /// <summary>Maps a column to a store-type name EFCore.Jet's type mapping source recognises.</summary>
    private static string StoreType(ColumnDef column) => column.Type switch
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
