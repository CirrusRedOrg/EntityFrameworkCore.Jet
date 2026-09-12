namespace LibRed.Formats;

/// <summary>
/// The <c>MSysQueries</c> row vocabulary that encodes a stored query (view / action query): the per-row
/// attribute codes, the operation (query-type) codes, the option flag bits, and the
/// <c>MSysObjects.Type</c> value for a query object. Shared by the read side (<c>JetCatalog</c>) and the write
/// side (<c>ViewCreator</c>) so the two can't drift. See spec §11.
/// </summary>
internal static class StoredQueryFormat
{
    /// <summary><c>MSysObjects.Type</c> value for a stored query / view object.</summary>
    public const short ObjectTypeQuery = 5;

    // MSysQueries per-row attribute codes.
    public const byte AttrType = 0x00;       // start record (Flag = 1 for SELECT)
    public const byte AttrOperation = 0x01;  // query type: Flag = one of the Operation* values below
    public const byte AttrParameter = 0x02;  // Name1 = param name, Flag = Jet type code
    public const byte AttrOption = 0x03;     // Flag = the Flag* option bits below (cumulative)
    public const byte AttrConnect = 0x04;    // connection string to an external table (pass-through)
    public const byte AttrTable = 0x05;      // named/derived table, UNION segment, or subquery
    public const byte AttrColumn = 0x06;     // Expression = column text
    public const byte AttrJoin = 0x07;       // Expression = condition, Flag = kind
    public const byte AttrWhere = 0x08;      // Expression = predicate text
    public const byte AttrGroupBy = 0x09;    // Expression = a GROUP BY column
    public const byte AttrHaving = 0x0A;     // Expression = an aggregate query's HAVING predicate
    public const byte AttrOrderBy = 0x0B;    // Expression = a sort column
    public const byte AttrComplex = 0x0C;    // complex data types (MVF / attachment / version history)
    public const byte AttrEnd = 0xFF;        // terminating row

    /// <summary>AttrType Flag value for a SELECT query.</summary>
    public const short QueryTypeSelect = 1;

    // AttrOperation Flag values -- the query KIND. Note SELECT is one of them: the row's presence does not
    // make a query an action query, and Access writes it on plain SELECTs too (221 of them across the sample
    // corpus). Measured against DAO's QueryDef.Type on every stored query in the corpus; 7 and 8 have no
    // sample there and come from the published MSysQueries tables.
    public const short OperationSelect = 1;      // DAO dbQSelect (0)
    public const short ActionMakeTable = 2;      // DAO dbQMakeTable (80) -- SELECT … INTO; target in Name1
    public const short ActionAppend = 3;         // DAO dbQAppend (64) -- target table in Name1
    public const short ActionUpdate = 4;         // DAO dbQUpdate (48)
    public const short ActionDelete = 5;         // DAO dbQDelete (32)
    public const short ActionCrosstab = 6;       // DAO dbQCrosstab (16) -- TRANSFORM
    public const short ActionDdl = 7;            // DAO dbQDDL (96) -- whole SQL in Expression
    public const short ActionPassThrough = 8;    // DAO dbQSQLPassThrough (112)
    public const short ActionUnion = 9;          // DAO dbQSetOperation (128)

    // AttrOption Flag bits. Cumulative, so test them as bits: 18 = DISTINCT TOP, 24 = DISTINCTROW TOP,
    // 48 = TOP PERCENT, 56 = DISTINCTROW TOP PERCENT. Flag 9 (OutputAllFields|DistinctRow) is what Access
    // writes for the auto-generated form/report record-source queries.
    public const short FlagOutputAllFields = 1;  // also UNION ALL on a UNION query
    public const short FlagDistinct = 2;
    public const short FlagOwnerAccess = 4;      // WITH OWNERACCESS OPTION
    public const short FlagDistinctRow = 8;
    public const short FlagTop = 0x10;           // the count is in Name1
    public const short FlagPercent = 0x20;       // only ever with FlagTop: TOP n PERCENT

    /// <summary>AttrColumn Flag bit marking an appended literal value.</summary>
    public const short AppendValueFlag = unchecked((short)0x8000);
}
