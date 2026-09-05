namespace LibRed.Formats;

/// <summary>
/// The <c>MSysQueries</c> row vocabulary that encodes a stored query (view / action query): the per-row
/// attribute codes, the query-type / DISTINCT / TOP flag values, the action-query sub-kinds, and the
/// <c>MSysObjects.Type</c> value for a query object. Shared by the read side (<c>JetCatalog</c>) and the write
/// side (<c>ViewCreator</c>) so the two can't drift. See spec §11.
/// </summary>
internal static class StoredQueryFormat
{
    /// <summary><c>MSysObjects.Type</c> value for a stored query / view object.</summary>
    public const short ObjectTypeQuery = 5;

    // MSysQueries per-row attribute codes.
    public const byte AttrType = 0x00;       // query type (Flag = 1 for SELECT)
    public const byte AttrAction = 0x01;     // action query: Flag 7 = DDL, Flag 3 = append
    public const byte AttrParameter = 0x02;  // Name1 = param name, Flag = Jet type code
    public const byte AttrFlag = 0x03;       // Flag = 2 for DISTINCT; Flag = 0x10 for TOP
    public const byte AttrTable = 0x05;      // named/derived table
    public const byte AttrColumn = 0x06;     // Expression = column text
    public const byte AttrJoin = 0x07;       // Expression = condition, Flag = kind
    public const byte AttrWhere = 0x08;      // Expression = predicate text
    public const byte AttrGroupBy = 0x09;    // Expression = a GROUP BY column
    public const byte AttrOrderBy = 0x0B;    // Expression = a sort column
    public const byte AttrEnd = 0xFF;        // terminating row

    /// <summary>AttrType Flag value for a SELECT query.</summary>
    public const short QueryTypeSelect = 1;

    /// <summary>AttrFlag Flag value marking the query DISTINCT.</summary>
    public const short FlagDistinct = 2;

    /// <summary>AttrFlag Flag value marking a TOP query (Name1 = the count as text).</summary>
    public const short FlagTop = 0x10;

    /// <summary>AttrAction Flag value for a data-definition (DDL) query (whole SQL in Expression).</summary>
    public const short ActionDdl = 7;

    /// <summary>AttrAction Flag value for an append (INSERT) query (target table in Name1).</summary>
    public const short ActionAppend = 3;

    /// <summary>AttrAction Flag value for a DELETE query.</summary>
    public const short ActionDelete = 5;

    /// <summary>AttrAction Flag value for an UPDATE query.</summary>
    public const short ActionUpdate = 4;

    /// <summary>AttrColumn Flag bit marking an appended literal value.</summary>
    public const short AppendValueFlag = unchecked((short)0x8000);
}
