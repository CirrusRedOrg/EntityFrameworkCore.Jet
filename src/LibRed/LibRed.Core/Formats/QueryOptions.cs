namespace LibRed.Formats;

/// <summary>
/// The <see cref="QueryAttribute.Option"/> row's <c>Flag</c> bits. Cumulative, so test them as bits: 18 =
/// DISTINCT TOP, 24 = DISTINCTROW TOP, 48 = TOP PERCENT, 56 = DISTINCTROW TOP PERCENT. Flag 9
/// (OutputAllFields | DistinctRow) is what Access writes for the auto-generated form/report record-source queries.
/// </summary>
[Flags]
internal enum QueryOptions : short
{
    None = 0,

    /// <summary>Also UNION ALL on a UNION query.</summary>
    OutputAllFields = 0x01,

    Distinct = 0x02,

    /// <summary>WITH OWNERACCESS OPTION.</summary>
    OwnerAccess = 0x04,

    DistinctRow = 0x08,

    /// <summary>TOP n: the count is in the row's <c>Name1</c>.</summary>
    Top = 0x10,

    /// <summary>Only ever with <see cref="Top"/>: TOP n PERCENT.</summary>
    Percent = 0x20,
}