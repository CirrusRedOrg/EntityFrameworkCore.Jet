using LibRed.Catalog;

namespace LibRed.Formats;

/// <summary>An <c>MSysQueries</c> row's <c>Attribute</c>: which part of the stored query the row holds (spec §11).</summary>
internal enum QueryAttribute : byte
{
    /// <summary>The start record (<c>Flag</c> = <see cref="StoredQuery.QueryTypeSelect"/> for a SELECT).</summary>
    Type = 0x00,

    /// <summary>The query's kind: <c>Flag</c> is a <see cref="QueryOperation"/>.</summary>
    Operation = 0x01,

    /// <summary>A declared parameter: <c>Name1</c> its name, <c>Flag</c> its Jet type code.</summary>
    Parameter = 0x02,

    /// <summary>The <see cref="QueryOptions"/> in <c>Flag</c>, cumulative across rows.</summary>
    Option = 0x03,

    /// <summary>The connection string to an external table (pass-through).</summary>
    Connect = 0x04,

    /// <summary>A named or derived table, a UNION segment, or a subquery.</summary>
    Table = 0x05,

    /// <summary>An output column: <c>Expression</c> is its text.</summary>
    Column = 0x06,

    /// <summary>A join: <c>Expression</c> the condition, <c>Flag</c> a <see cref="ViewJoinType"/>.</summary>
    Join = 0x07,

    /// <summary>The predicate, in <c>Expression</c>.</summary>
    Where = 0x08,

    /// <summary>A GROUP BY column, in <c>Expression</c>.</summary>
    GroupBy = 0x09,

    /// <summary>An aggregate query's HAVING predicate, in <c>Expression</c>.</summary>
    Having = 0x0A,

    /// <summary>A sort column, in <c>Expression</c>.</summary>
    OrderBy = 0x0B,

    /// <summary>Complex data types (multi-value / attachment / version history).</summary>
    Complex = 0x0C,

    /// <summary>The terminating row.</summary>
    End = 0xFF,
}