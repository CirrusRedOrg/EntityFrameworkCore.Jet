namespace LibRed.Formats;

/// <summary>
/// DAO's <c>QueryDef.Type</c>, which a stored query's <c>MSysObjects.Flags</c> carries beside
/// <see cref="ObjectAttributes.QueryDef"/> (verified vs ACE for crosstab, delete, update, append, make-table and
/// data-definition) — not the <see cref="QueryOperation"/> the MSysQueries action row carries, which numbers the
/// kinds differently.
/// </summary>
internal enum QueryDefType
{
    Select = 0,
    Crosstab = 16,
    Delete = 32,
    Update = 48,
    Append = 64,
    MakeTable = 80,
    DataDefinition = 96,
}