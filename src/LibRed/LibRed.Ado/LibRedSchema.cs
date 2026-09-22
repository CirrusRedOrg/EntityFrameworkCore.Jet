using LibRed.Engine.Schema;
using LibRed.Sql;
using System.Data;
using System.Data.Common;

namespace LibRed.Data;

/// <summary>
/// ADO.NET's metadata collections for an open connection. The catalog collections come from
/// <see cref="SchemaRowsets"/>, shaped as ACE's OLE DB provider serves them; the five collections the
/// framework defines (<c>MetaDataCollections</c>, <c>DataSourceInformation</c>, <c>DataTypes</c>,
/// <c>Restrictions</c>, <c>ReservedWords</c>) are built here, describing LibRed itself.
/// </summary>
internal static class LibRedSchema
{
    public const string MetaDataCollections = "MetaDataCollections";
    public const string DataSourceInformation = "DataSourceInformation";
    public const string DataTypes = "DataTypes";
    public const string Restrictions = "Restrictions";
    public const string ReservedWords = "ReservedWords";

    /// <summary>Every collection served, the framework's own first, then the catalog ones — the order ACE
    /// lists them in.</summary>
    public static IReadOnlyList<string> Names { get; } =
        [MetaDataCollections, DataSourceInformation, DataTypes, Restrictions, ReservedWords, .. SchemaRowsets.Names];

    /// <summary>How many identifier parts a collection's rows are named by, as ACE reports: a column takes
    /// catalog, schema, table and column; a table one fewer.</summary>
    private static readonly Dictionary<string, int> IdentifierParts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tables"] = 3,
        ["Columns"] = 4,
        ["Indexes"] = 4,
        ["Views"] = 3,
        ["Procedures"] = 3,
        ["ForeignKeys"] = 3,
        ["PrimaryKeys"] = 3,
        ["TableConstraints"] = 3,
        ["KeyColumnUsage"] = 4,
        ["ConstraintColumnUsage"] = 4,
        ["ReferentialConstraints"] = 3,
        ["CheckConstraints"] = 3,
        ["Statistics"] = 3,
        ["ProcedureParameters"] = 4,
        ["ViewColumns"] = 4,
    };

    /// <summary>Each collection's restrictions, in ACE's order — note that Indexes takes the table name last,
    /// after the index name and type. A restriction names the column its value filters.</summary>
    private static readonly (string Collection, string Restriction)[] RestrictionList =
    [
        ("Columns", "TABLE_CATALOG"), ("Columns", "TABLE_SCHEMA"), ("Columns", "TABLE_NAME"), ("Columns", "COLUMN_NAME"),
        ("Indexes", "TABLE_CATALOG"), ("Indexes", "TABLE_SCHEMA"), ("Indexes", "INDEX_NAME"), ("Indexes", "TYPE"),
        ("Indexes", "TABLE_NAME"),
        ("Procedures", "PROCEDURE_CATALOG"), ("Procedures", "PROCEDURE_SCHEMA"), ("Procedures", "PROCEDURE_NAME"),
        ("Procedures", "PROCEDURE_TYPE"),
        ("Tables", "TABLE_CATALOG"), ("Tables", "TABLE_SCHEMA"), ("Tables", "TABLE_NAME"), ("Tables", "TABLE_TYPE"),
        ("Views", "TABLE_CATALOG"), ("Views", "TABLE_SCHEMA"), ("Views", "TABLE_NAME"),
        // The relational rowsets, restricted as OLE DB restricts them.
        ("ForeignKeys", "PK_TABLE_CATALOG"), ("ForeignKeys", "PK_TABLE_SCHEMA"), ("ForeignKeys", "PK_TABLE_NAME"),
        ("ForeignKeys", "FK_TABLE_CATALOG"), ("ForeignKeys", "FK_TABLE_SCHEMA"), ("ForeignKeys", "FK_TABLE_NAME"),
        ("PrimaryKeys", "TABLE_CATALOG"), ("PrimaryKeys", "TABLE_SCHEMA"), ("PrimaryKeys", "TABLE_NAME"),
        ("TableConstraints", "CONSTRAINT_CATALOG"), ("TableConstraints", "CONSTRAINT_SCHEMA"),
        ("TableConstraints", "CONSTRAINT_NAME"), ("TableConstraints", "TABLE_CATALOG"),
        ("TableConstraints", "TABLE_SCHEMA"), ("TableConstraints", "TABLE_NAME"), ("TableConstraints", "CONSTRAINT_TYPE"),
        ("KeyColumnUsage", "CONSTRAINT_CATALOG"), ("KeyColumnUsage", "CONSTRAINT_SCHEMA"),
        ("KeyColumnUsage", "CONSTRAINT_NAME"), ("KeyColumnUsage", "TABLE_CATALOG"),
        ("KeyColumnUsage", "TABLE_SCHEMA"), ("KeyColumnUsage", "TABLE_NAME"), ("KeyColumnUsage", "COLUMN_NAME"),
        ("ConstraintColumnUsage", "TABLE_CATALOG"), ("ConstraintColumnUsage", "TABLE_SCHEMA"),
        ("ConstraintColumnUsage", "TABLE_NAME"), ("ConstraintColumnUsage", "COLUMN_NAME"),
        ("ConstraintColumnUsage", "CONSTRAINT_CATALOG"), ("ConstraintColumnUsage", "CONSTRAINT_SCHEMA"),
        ("ConstraintColumnUsage", "CONSTRAINT_NAME"),
        ("ReferentialConstraints", "CONSTRAINT_CATALOG"), ("ReferentialConstraints", "CONSTRAINT_SCHEMA"),
        ("ReferentialConstraints", "CONSTRAINT_NAME"),
        ("CheckConstraints", "CONSTRAINT_CATALOG"), ("CheckConstraints", "CONSTRAINT_SCHEMA"),
        ("CheckConstraints", "CONSTRAINT_NAME"),
        ("Statistics", "TABLE_CATALOG"), ("Statistics", "TABLE_SCHEMA"), ("Statistics", "TABLE_NAME"),
        ("ProcedureParameters", "PROCEDURE_CATALOG"), ("ProcedureParameters", "PROCEDURE_SCHEMA"),
        ("ProcedureParameters", "PROCEDURE_NAME"), ("ProcedureParameters", "PARAMETER_NAME"),
        ("ViewColumns", "VIEW_CATALOG"), ("ViewColumns", "VIEW_SCHEMA"), ("ViewColumns", "VIEW_NAME"),
        ("ViewColumns", "COLUMN_NAME"),
    ];

    /// <summary>A Jet file holds one nameless catalog and no schemas, so these restrictions match everything
    /// rather than filtering on the null every row carries.</summary>
    private static bool IsCatalogOrSchema(string restriction) =>
        restriction.EndsWith("_CATALOG", StringComparison.Ordinal) || restriction.EndsWith("_SCHEMA", StringComparison.Ordinal);

    /// <summary>Builds a collection, filtered by <paramref name="restrictions"/> (a null entry, or none at
    /// all, matches everything).</summary>
    public static DataTable Get(string collection, string?[]? restrictions, JetDatabase database)
    {
        string name = Names.FirstOrDefault(n => n.Equals(collection, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"There is no metadata collection named '{collection}'.", nameof(collection));

        var allowed = RestrictionList.Where(r => r.Collection.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (restrictions is not null && restrictions.Length > allowed.Count)
            throw new ArgumentException(
                $"The '{name}' collection takes {allowed.Count} restrictions; {restrictions.Length} were supplied.",
                nameof(restrictions));

        return name switch
        {
            MetaDataCollections => BuildMetaDataCollections(),
            DataSourceInformation => BuildDataSourceInformation(database),
            DataTypes => BuildDataTypes(database),
            Restrictions => BuildRestrictions(),
            ReservedWords => BuildReservedWords(),
            _ => BuildCatalogCollection(name, restrictions, database),
        };
    }

    private static DataTable BuildCatalogCollection(string name, string?[]? restrictions, JetDatabase database)
    {
        var table = new DataTable(name) { Locale = System.Globalization.CultureInfo.InvariantCulture };
        IReadOnlyList<string> columns = SchemaRowsets.ColumnsOf(name);
        IReadOnlyList<Type> types = SchemaRowsets.ColumnTypesOf(name);
        for (int i = 0; i < columns.Count; i++)
            table.Columns.Add(columns[i], types[i]);

        var allowed = RestrictionList.Where(r => r.Collection.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (object?[] row in SchemaRowsets.Rows(name, database))
        {
            if (!Matches(row, columns, allowed, restrictions)) continue;
            table.Rows.Add(row.Select(v => v ?? DBNull.Value).ToArray());
        }

        return table;
    }

    /// <summary>Whether a row passes every supplied restriction. Values compare as text, case-insensitively,
    /// because Jet identifiers are.</summary>
    private static bool Matches(
        object?[] row, IReadOnlyList<string> columns, List<(string Collection, string Restriction)> allowed, string?[]? restrictions)
    {
        if (restrictions is null) return true;

        for (int i = 0; i < restrictions.Length; i++)
        {
            if (restrictions[i] is not { } wanted) continue;
            string restriction = allowed[i].Restriction;
            if (IsCatalogOrSchema(restriction)) continue;

            int column = IndexOf(columns, restriction);
            if (column < 0) continue;
            if (!string.Equals(row[column]?.ToString(), wanted, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    private static int IndexOf(IReadOnlyList<string> columns, string name)
    {
        for (int i = 0; i < columns.Count; i++)
            if (columns[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static DataTable BuildMetaDataCollections()
    {
        var table = Empty(MetaDataCollections,
            ("CollectionName", typeof(string)), ("NumberOfRestrictions", typeof(int)), ("NumberOfIdentifierParts", typeof(int)));
        foreach (string name in Names)
            table.Rows.Add(name,
                RestrictionList.Count(r => r.Collection.Equals(name, StringComparison.OrdinalIgnoreCase)),
                IdentifierParts.GetValueOrDefault(name));
        return table;
    }

    private static DataTable BuildRestrictions()
    {
        var table = Empty(Restrictions,
            ("CollectionName", typeof(string)), ("RestrictionName", typeof(string)),
            ("RestrictionDefault", typeof(string)), ("RestrictionNumber", typeof(int)));
        string? collection = null;
        int number = 0;
        foreach ((string name, string restriction) in RestrictionList)
        {
            number = name == collection ? number + 1 : 1;
            collection = name;
            table.Rows.Add(name, restriction, DBNull.Value, number);
        }
        return table;
    }

    private static DataTable BuildReservedWords()
    {
        var table = Empty(ReservedWords, ("ReservedWord", typeof(string)));
        foreach (string word in SqlKeywords.Reserved)
            table.Rows.Add(word);
        return table;
    }

    private static DataTable BuildDataSourceInformation(JetDatabase database)
    {
        var table = Empty(DataSourceInformation,
            ("CompositeIdentifierSeparatorPattern", typeof(string)), ("DataSourceProductName", typeof(string)),
            ("DataSourceProductVersion", typeof(string)), ("DataSourceProductVersionNormalized", typeof(string)),
            ("GroupByBehavior", typeof(GroupByBehavior)), ("IdentifierPattern", typeof(string)),
            ("IdentifierCase", typeof(IdentifierCase)), ("OrderByColumnsInSelect", typeof(bool)),
            ("ParameterMarkerFormat", typeof(string)), ("ParameterMarkerPattern", typeof(string)),
            ("ParameterNameMaxLength", typeof(int)), ("ParameterNamePattern", typeof(string)),
            ("QuotedIdentifierPattern", typeof(string)), ("QuotedIdentifierCase", typeof(IdentifierCase)),
            ("StatementSeparatorPattern", typeof(string)), ("StringLiteralPattern", typeof(string)),
            ("SupportedJoinOperators", typeof(SupportedJoinOperators)));

        table.Rows.Add(
            DBNull.Value,                       // no catalog or schema, so nothing separates identifier parts
            "LibRed",
            FormatName(database.Format.Version),
            // Sortable and fixed-width, as the collection requires: the file's format version byte, so a later
            // format never sorts below an earlier one.
            $"{(byte)database.Format.Version:00}.00.0000",
            GroupByBehavior.MustContainAll,
            @"[^ ][^\.!`\[\]]*",                // as ACE describes a Jet identifier
            IdentifierCase.Insensitive,
            false,                              // ORDER BY may name a column the SELECT does not
            "?",
            @"\?",
            0,                                  // parameters are positional, so a name has no length
            DBNull.Value,
            "`(([^`]|``)*)`",                   // backticks, doubled to escape one
            IdentifierCase.Insensitive,
            ";",
            "'(([^']|'')*)'",
            SupportedJoinOperators.Inner | SupportedJoinOperators.LeftOuter
                | SupportedJoinOperators.RightOuter | SupportedJoinOperators.FullOuter);
        return table;
    }

    /// <summary>The types a column can have, as ACE lists them — its own type names, each with the OLE DB type
    /// code, the CLR type it reads as, and the literal syntax — plus the two types ACE's own list never learned.
    /// </summary>
    private static DataTable BuildDataTypes(JetDatabase database)
    {
        var table = Empty(DataTypes,
            ("TypeName", typeof(string)), ("ProviderDbType", typeof(int)), ("ColumnSize", typeof(long)),
            ("CreateFormat", typeof(string)), ("CreateParameters", typeof(string)), ("DataType", typeof(string)),
            ("IsAutoIncrementable", typeof(bool)), ("IsBestMatch", typeof(bool)), ("IsCaseSensitive", typeof(bool)),
            ("IsFixedLength", typeof(bool)), ("IsFixedPrecisionScale", typeof(bool)), ("IsLong", typeof(bool)),
            ("IsNullable", typeof(bool)), ("IsSearchable", typeof(bool)), ("IsSearchableWithLike", typeof(bool)),
            ("IsUnsigned", typeof(bool)), ("MaximumScale", typeof(short)), ("MinimumScale", typeof(short)),
            ("IsConcurrencyType", typeof(bool)), ("IsLiteralSupported", typeof(bool)),
            ("LiteralPrefix", typeof(string)), ("LiteralSuffix", typeof(string)), ("NativeDataType", typeof(short)));

        void Add(string name, int providerType, long size, string? createParameters, Type clr, bool autoIncrement,
            bool fixedLength, bool fixedPrecisionScale, bool isLong, bool nullable, bool unsigned,
            short? maximumScale, short? minimumScale, string? prefix, string? suffix, short nativeType) =>
            table.Rows.Add(name, providerType, size, DBNull.Value, (object?)createParameters ?? DBNull.Value,
                clr.FullName, autoIncrement, DBNull.Value, false, fixedLength, fixedPrecisionScale, isLong,
                nullable, true, true, unsigned,
                (object?)maximumScale ?? DBNull.Value, (object?)minimumScale ?? DBNull.Value,
                DBNull.Value, DBNull.Value, (object?)prefix ?? DBNull.Value, (object?)suffix ?? DBNull.Value, nativeType);

        Add("Short", 2, 5, null, typeof(short), false, true, true, false, true, false, null, null, null, null, 2);
        Add("Long", 3, 10, null, typeof(int), true, true, true, false, true, false, null, null, null, null, 3);
        Add("Single", 4, 7, null, typeof(float), false, true, false, false, true, false, null, null, null, null, 4);
        Add("Double", 5, 15, null, typeof(double), false, true, false, false, true, false, null, null, null, null, 5);
        Add("Currency", 6, 19, null, typeof(decimal), false, true, true, false, true, false, null, null, null, null, 6);
        Add("DateTime", 7, 8, null, typeof(DateTime), false, true, true, false, true, true, null, null, "#", "#", 7);
        Add("Bit", 11, 2, null, typeof(bool), false, true, true, false, false, true, null, null, null, null, 11);
        Add("Byte", 17, 3, null, typeof(byte), false, true, true, false, true, true, null, null, null, null, 17);
        Add("GUID", 72, 16, null, typeof(Guid), false, true, true, false, true, true, null, null, null, null, 72);
        Add("BigBinary", 204, 4000, null, typeof(byte[]), false, false, false, false, true, true, null, null, "0x", null, 128);
        Add("LongBinary", 205, 1073741823, null, typeof(byte[]), false, false, true, true, true, true, null, null, "0x", null, 128);
        Add("VarBinary", 204, 510, "max length", typeof(byte[]), false, false, true, false, true, true, null, null, "0x", null, 128);
        Add("LongText", 203, 536870910, null, typeof(string), false, false, true, true, true, true, null, null, "'", "'", 130);
        Add("VarChar", 202, 255, "max length", typeof(string), false, false, true, false, true, true, null, null, "'", "'", 130);
        Add("Decimal", 131, 28, "precision,scale", typeof(decimal), false, true, true, false, true, false, 28, 0, null, null, 131);

        // The fixed-length text and binary forms, which ACE's list omits although the engine has both: a
        // CHAR(n)/NCHAR(n) column is stored fixed, and so is BINARY(n) — even though ACE then reports the
        // binary one as variable. Without these rows a fixed column's TYPE_NAME would have nothing to join to.
        Add("Char", 130, 255, "max length", typeof(string), false, true, true, false, true, true, null, null, "'", "'", 130);
        Add("Binary", 128, 510, "max length", typeof(byte[]), false, true, true, false, true, true, null, null, "0x", null, 128);

        // BIGINT and DATETIME2 arrived after ACE's OLE DB provider, whose list still omits them although it
        // reports columns of both. Any ACCDB can hold them: a file too old for one is raised to the format it
        // needs when the column is created, which is what Access itself does. A Jet 3/4 .mdb cannot, so an
        // .mdb lists exactly the types ACE does.
        if (database.Format.Version >= LibRed.Formats.JetVersion.Version12_2007)
        {
            Add("BigInt", 20, 19, null, typeof(long), false, true, true, false, true, false, null, null, null, null, 20);
            Add("DateTime2", 135, 42, null, typeof(DateTime), false, true, true, false, true, true, null, null, "#", "#", 135);
        }

        return table;
    }

    /// <summary>The engine version the open file's format belongs to, named as Access names it.</summary>
    private static string FormatName(LibRed.Formats.JetVersion version) => version switch
    {
        LibRed.Formats.JetVersion.Version3 => "Jet 3 (Access 97)",
        LibRed.Formats.JetVersion.Version4 => "Jet 4 (Access 2000-2003)",
        LibRed.Formats.JetVersion.Version12_2007 => "ACE 12 (Access 2007)",
        LibRed.Formats.JetVersion.Version14_2010 => "ACE 14 (Access 2010)",
        LibRed.Formats.JetVersion.Version15_2013 => "ACE 15 (Access 2013)",
        LibRed.Formats.JetVersion.Version16_2016 => "ACE 16 (Access 2016)",
        LibRed.Formats.JetVersion.Version17_2019 => "ACE 17 (Access 2019)",
        _ => version.ToString(),
    };

    private static DataTable Empty(string name, params (string Name, Type Type)[] columns)
    {
        var table = new DataTable(name) { Locale = System.Globalization.CultureInfo.InvariantCulture };
        foreach ((string column, Type type) in columns)
            table.Columns.Add(column, type);
        return table;
    }
}