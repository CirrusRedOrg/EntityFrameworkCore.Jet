namespace LibRed.Formats;

/// <summary>
/// The <see cref="QueryAttribute.Operation"/> row's <c>Flag</c> — the query KIND. SELECT is one of them: the row's
/// presence does not make a query an action query, and Access writes it on plain SELECTs too (221 of them across
/// the sample corpus). Measured against DAO's <c>QueryDef.Type</c> on every stored query in the corpus;
/// <see cref="Ddl"/> and <see cref="PassThrough"/> have no sample there and come from the published MSysQueries
/// tables.
/// </summary>
internal enum QueryOperation : short
{
    /// <summary>DAO <c>dbQSelect</c>.</summary>
    Select = 1,

    /// <summary>DAO <c>dbQMakeTable</c>: <c>SELECT … INTO</c>, the target in <c>Name1</c>.</summary>
    MakeTable = 2,

    /// <summary>DAO <c>dbQAppend</c>: the target table in <c>Name1</c>.</summary>
    Append = 3,

    /// <summary>DAO <c>dbQUpdate</c>.</summary>
    Update = 4,

    /// <summary>DAO <c>dbQDelete</c>.</summary>
    Delete = 5,

    /// <summary>DAO <c>dbQCrosstab</c>: <c>TRANSFORM</c>.</summary>
    Crosstab = 6,

    /// <summary>DAO <c>dbQDDL</c>: the whole SQL in <c>Expression</c>.</summary>
    Ddl = 7,

    /// <summary>DAO <c>dbQSQLPassThrough</c>.</summary>
    PassThrough = 8,

    /// <summary>DAO <c>dbQSetOperation</c>: a UNION.</summary>
    Union = 9,
}