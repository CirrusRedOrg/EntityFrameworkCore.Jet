using System.Data.Common;
using System.Text.RegularExpressions;
using EntityFrameworkCore.Jet.Internal; // FK scaffolding-logger extensions (ForeignKeyFound, …)
using EntityFrameworkCore.Jet.Metadata.Internal; // JetAnnotationNames (identity seed/increment)
using LibRed;
using LibRed.Catalog;
using LibRed.Data;
using LibRed.Engine.Schema; // shared store-type/nullability derivations (JetStoreType) — kept in sync with INFORMATION_SCHEMA
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;

namespace EntityFrameworkCore.LibRed.Scaffolding.Internal;

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
            string parsed = StripDelimiters(table);
            if (databaseModel.Tables.All(t => !string.Equals(t.Name, parsed, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.MissingTableWarning(table);
            }
        }

        // Access has no schemas, so every requested schema is "missing" — warn, matching EFCore.Jet's factory.
        foreach (string schema in options.Schemas
                     .Where(s => !string.IsNullOrWhiteSpace(s))
                     .Except(databaseModel.Tables.Select(t => t.Schema ?? string.Empty), StringComparer.OrdinalIgnoreCase))
        {
            _logger.MissingSchemaWarning(schema);
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
                // Store type + nullability come from the shared JetStoreType so INFORMATION_SCHEMA and this
                // scaffolder can never report a column differently (see JetStoreType).
                string storeType = JetStoreType.StoreType(column);
                int maxLength = column.Type == JetDataType.Text ? Math.Max(1, column.Length / 2) : 0;
                bool nullable = JetStoreType.IsNullable(column);

                // DefaultValue is the expression's source text (e.g. "0", "'hi'"), read from the same blob.
                string? defaultValueSql = column.DefaultValue;

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
                    defaultValueSql,
                    null,  // computedValue
                    null); // computed-is-stored

                // Identity seed/increment (matching EFCore.Jet's scaffolder), read from the column's COUNTER
                // config; a non-identity column carries a null seed/increment as Jet's does.
                int? identitySeed = column.IsAutoNumber ? column.Seed : null;
                int? identityIncrement = column.IsAutoNumber ? column.Increment : null;

                var databaseColumn = new DatabaseColumn
                {
                    Table = table,
                    Name = column.Name,
                    StoreType = storeType,
                    IsNullable = nullable,
                    // Report the default both ways, as EFCore.Jet's scaffolder does: DefaultValueSql is the raw
                    // stored expression text (what the migrations tests assert against), DefaultValue is that text
                    // parsed to a CLR value when it's a simple literal (so the scaffolded model uses HasDefaultValue).
                    DefaultValueSql = defaultValueSql,
                    DefaultValue = ParseDefaultValue(column.Type, defaultValueSql),
                    ValueGenerated = column.IsAutoNumber ? ValueGenerated.OnAdd : null,
                };

                databaseColumn[JetAnnotationNames.IdentitySeed] = identitySeed;
                databaseColumn[JetAnnotationNames.IdentityIncrement] = identityIncrement;
                databaseColumn[JetAnnotationNames.Identity] = $"(${identitySeed}, ${identityIncrement})";

                table.Columns.Add(databaseColumn);
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
                // A UNIQUE constraint and a UNIQUE index are byte-identical in Jet (verified vs ACE), so they
                // can't be told apart structurally. Disambiguate by name like EFCore.Jet does: a unique index
                // named `IX_…` (EF's index convention) is a unique *index*; any other unique index (EF names an
                // alternate key `AK_…`) is a unique *constraint*.
                else if (index.IsUnique && !index.Name.StartsWith("IX_", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.UniqueConstraintFound(index.Name, table.Name);
                    var unique = new DatabaseUniqueConstraint { Table = table, Name = index.Name };
                    foreach (DatabaseColumn c in columns) unique.Columns.Add(c);
                    table.UniqueConstraints.Add(unique);
                }
                else if (IsForeignKeyBackingIndex(database, table, index))
                {
                    // A relationship's own child (FK) index is named after the relationship and isn't a
                    // standalone user index — it's part of the foreign key, surfaced via GetRelations. Real
                    // Jet scaffolding doesn't report it either; surfacing it makes the RelationalDatabaseCleaner
                    // emit a DROP INDEX for it, which ACE (and LibRed) reject while the relationship still
                    // exists (the cleaner drops the relationship by dropping the table, not the index).
                }
                else
                {
                    _logger.IndexFound(index.Name, table.Name, index.IsUnique);
                    // Surface the index's null-handling as the scaffolded Filter, as EFCore.Jet does: WITH IGNORE
                    // NULL → "IGNORE NULLS" (wins), else a Required (DISALLOW NULL) index → "DISALLOW NULL".
                    string? filter = index.IgnoreNulls ? "IGNORE NULLS" : index.Required ? "DISALLOW NULL" : null;
                    var dbIndex = new DatabaseIndex { Table = table, Name = index.Name, IsUnique = index.IsUnique, Filter = filter };
                    // Carry the per-column sort direction (IsDescending, one bool per column) so scaffolding
                    // round-trips a DESC / mixed-order index — otherwise the list stays empty and a
                    // change-sort-order migration can't see the ordering.
                    foreach ((ColumnDef col, bool ascending) in index.Columns)
                    {
                        dbIndex.Columns.Add(table.Columns.First(dc => dc.Name == col.Name));
                        dbIndex.IsDescending.Add(!ascending);
                    }
                    table.Indexes.Add(dbIndex);
                }
            }
        }
    }

    /// <summary>True if this non-unique index is a relationship's child (FK) backing index — named after a
    /// relationship for which this table is the referencing (child) side, as both ACE and LibRed create it.</summary>
    private static bool IsForeignKeyBackingIndex(JetDatabase database, DatabaseTable table, IndexDef index)
        => database.Catalog.Relationships.Any(r =>
               string.Equals(r.Table, table.Name, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(r.Name, index.Name, StringComparison.OrdinalIgnoreCase));

    private void GetRelations(JetDatabase database, IReadOnlyList<DatabaseTable> tables)
    {
        DatabaseTable? Find(string name)
            => tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        foreach (ForeignKey relation in database.Catalog.Relationships)
        {
            DatabaseTable? referencingTable = Find(relation.Table);
            if (referencingTable is null) continue;

            // Jet supports ON DELETE NO ACTION / CASCADE / SET NULL (read from MSysRelationships.grbit +
            // the index-info action byte). EF's scaffolding models OnDelete only (no OnUpdate).
            ReferentialAction onDeleteAction = relation.CascadeDelete ? ReferentialAction.Cascade
                : relation.DeleteSetNull ? ReferentialAction.SetNull
                : ReferentialAction.NoAction;
            string onDelete = onDeleteAction switch
            {
                ReferentialAction.Cascade => "CASCADE",
                ReferentialAction.SetNull => "SET NULL",
                _ => "NO ACTION",
            };

            // Read the whole relation record — the full column set EFCore.Jet's JetDatabaseModelFactory /
            // AdoxSchema surface (INFORMATION_SCHEMA.RELATIONS ON_UPDATE / IS_ENFORCED / IS_INHERITED) — even
            // though EF's DatabaseForeignKey models only OnDelete. Read for parity, not consumed by EF.
            string onUpdate = relation.CascadeUpdate ? "CASCADE" : "NO ACTION";
            bool isEnforced = relation.IsEnforced;
            bool isInherited = relation.IsInherited;
            _ = (onUpdate, isEnforced, isInherited);

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
                OnDelete = onDeleteAction,
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
        var wanted = tables.Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(StripDelimiters)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return wanted.Count > 0 ? name => wanted.Contains(name) : null;
    }

    /// <summary>Strips a leading/trailing <c>`..`</c> or <c>[..]</c> identifier delimiter so a filter given as
    /// <c>`K2`</c> matches the bare table name <c>K2</c> (mirrors EFCore.Jet's <c>Parse</c>).</summary>
    private static string StripDelimiters(string name)
    {
        name = name.Trim();
        return name.Length >= 2 && ((name[0] == '`' && name[^1] == '`') || (name[0] == '[' && name[^1] == ']'))
            ? name[1..^1]
            : name;
    }

    /// <summary>Converts a column's stored default-value text (e.g. <c>"0"</c>, <c>"'hi'"</c>, <c>"-1"</c>) to
    /// the CLR literal it represents, coerced to the column's type. Jet/ACE defaults are literal values, not
    /// SQL expressions, so this becomes <c>DatabaseColumn.DefaultValue</c>. Unparseable text (e.g. a function
    /// like <c>Now()</c>) is returned as-is.</summary>
    private static object? ParseDefaultValue(JetDataType type, string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            return null;

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            return type switch
            {
                // Jet booleans are -1/0; also accept True/False.
                JetDataType.Boolean => int.TryParse(text, out int i) ? i != 0 : bool.Parse(text),
                JetDataType.Byte => byte.Parse(text, ci),
                JetDataType.Int16 => short.Parse(text, ci),
                JetDataType.Int32 => int.Parse(text, ci),
                JetDataType.Int64 => long.Parse(text, ci),
                JetDataType.Single => float.Parse(text, ci),
                JetDataType.Double => double.Parse(text, ci),
                JetDataType.Currency or JetDataType.FixedPoint => decimal.Parse(text, ci),
                JetDataType.Guid => Guid.Parse(Unquote(text).Trim('{', '}')),
                // Temporal defaults: a bare date → DateOnly, date+time → DateTime, a time span → TimeOnly/TimeSpan
                // (matching EFCore.Jet's scaffolder). A function default (Now()/Date()) matches none → raw text.
                JetDataType.DateTime or JetDataType.DateTimeExtended => ParseTemporalDefault(text) ?? (object)text,
                // A string literal is single-quoted with doubled inner quotes: 'Bon app''' → Bon app'.
                JetDataType.Text or JetDataType.Memo => ParseStringDefault(text),
                _ => text,
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return text; // e.g. a default expression such as Now()/Date() — keep the raw text
        }
    }

    private static string Unquote(string s)
        => s.Length >= 2 && s[0] == '\'' && s[^1] == '\''
            ? s[1..^1].Replace("''", "'")
            : s;

    /// <summary>A string-column default is normally just the unquoted text, but a literal that reads as a
    /// <see cref="DateTimeOffset"/> is surfaced as one: Jet has no datetimeoffset type, so EF models such a
    /// column as text yet still expects the parsed value (matches EFCore.Jet's scaffolder).</summary>
    private static object ParseStringDefault(string text)
    {
        string s = Unquote(text);
        return DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out DateTimeOffset dto) ? dto : s;
    }

    /// <summary>Parses a temporal default's text into the CLR type it denotes — DateOnly (date only), DateTime
    /// (date + time), TimeOnly/TimeSpan (a time), matching EFCore.Jet's scaffolder. Returns null when the text is
    /// not a recognised temporal literal (e.g. a function like <c>Now()</c>), so the caller keeps the raw text.</summary>
    private static object? ParseTemporalDefault(string text)
    {
        string s = Unquote(text.Trim()).Trim('#').Trim();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var t = TimeSpan.FromMilliseconds(1000);
        if (Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}$", default, t))
            return DateOnly.Parse(s, ci);
        if (Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(\.\d{1,7})?$", default, t))
            return DateTime.Parse(s, ci);
        if (Regex.IsMatch(s, @"^-?(\d+\.)?\d{2}:\d{2}:\d{2}(\.\d{1,7})?$", default, t)
            && TimeSpan.TryParse(s, ci, out var ts))
            return ts >= TimeOnly.MinValue.ToTimeSpan() && ts <= TimeOnly.MaxValue.ToTimeSpan()
                ? TimeOnly.FromTimeSpan(ts) : ts;
        return null;
    }

}
