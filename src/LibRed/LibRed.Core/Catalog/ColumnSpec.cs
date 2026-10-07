namespace LibRed.Catalog;

/// <summary>A column to create: its name, type, and (for fixed/text) byte length.</summary>
// AutoNumber (COUNTER) seed and increment — the first generated id is Seed, then +Increment each row.
// Default 1/1 (a plain COUNTER). Stored in the TDEF header: last-value 0x14 = Seed-Increment, 0x18 = Increment.
// An explicit column id, else the column's position is used. Access's own catalog tables declare their
// columns in an order that is not their id order (JetDatabase); null for an ordinary CREATE/ADD column.
// The catalog flag bits (0x0F) Access sets on the columns of its own catalog — MSysObjects, MSysACEs,
// MSysQueries, MSysRelationships and MSysComplexColumns: 0x10 on every one, and 0x20 as well on the two that
// hold a security identifier (MSysObjects.Owner, MSysACEs.SID). Every other column leaves them clear, the
// other MSys* tables' included (verified; see page-02b-columns.md).
// WITH COMPRESSION on a Text/Memo column: the 0x10 extended flag bit 0x01. Off unless asked for, which
// is what ACE does for a column declared without it (LongTextStorageAccessTests).
// A calculated column (§3.4a): the expression, and the type the caller DECLARED for it. Both null for an
// ordinary column. They are separate from Type because Type carries the descriptor's *promoted* storage
// type, which need not be the result type at all — build one with Calculated() rather than by hand.
// A column of a table the engine creates for itself, which leaves 0x09 — the second copy of the column id —
// zero. A catalog column (SystemFlags) is one already; this marks the rest, such as the MSysComplexType_*
// templates, which carry no catalog flag.
// Extended flag bits (0x10) LibRed does not model, to set on a created column: 0x10 on an attachment value
// column (FileData, FileFlags, FileName, FileTimeStamp, FileType, FileURL).
public sealed record ColumnSpec(
    string Name,
    JetDataType Type,
    int Length,
    bool IsFixedLength,
    bool IsAutoNumber = false,
    byte Precision = 0,
    byte Scale = 0,
    bool IsNullable = true,
    int Seed = 1,
    int Increment = 1,
    int? ColumnId = null,
    byte SystemFlags = 0,
    bool SupportsCompressedUnicode = false,
    string? CalculatedExpression = null,
    JetDataType? CalculatedResultType = null,
    bool IsEngineColumn = false,
    byte ExtendedFlags = 0)
{
    /// <summary>A calculated column of <paramref name="resultType"/> computing <paramref name="expression"/>.
    /// ACE stores one **always variable-length**, with the descriptor carrying the *promoted* type and a
    /// constant declared length, and the real result type living in the table's property blob — get any of
    /// those wrong and Access reads the payload at the wrong width, so this is the only supported way to
    /// build one.</summary>
    // `Backtick` quoting is SQL's, not the expression service's, and ACE cannot read it — normalise
    // here so every route in (SQL, or a direct caller) stores something ACE can evaluate.
    public static ColumnSpec Calculated(string name, JetDataType resultType, string expression) =>
        new(name, PromotedType(resultType), CalculatedLength(resultType), IsFixedLength: false,
            CalculatedExpression: Storage.Calculated.CalculatedExpression.NormaliseIdentifierQuoting(expression),
            CalculatedResultType: resultType);

    /// <summary>The descriptor's storage type: ACE widens the result type to the next in its family, which is
    /// why the payload's own length is what says how to decode it (§3.4a).</summary>
    internal static JetDataType PromotedType(JetDataType resultType) => resultType switch
    {
        JetDataType.Boolean => JetDataType.Int16,
        JetDataType.Byte or JetDataType.Int16 => JetDataType.Int32,
        JetDataType.Single => JetDataType.Double,
        // Memo, Guid and Binary all land on Text — only their declared LENGTH tells them apart.
        JetDataType.Memo or JetDataType.Guid or JetDataType.Binary => JetDataType.Text,
        _ => resultType,
    };

    /// <summary>ACE's constant declared length for a calculated column, measured across every type DAO will
    /// create: <b>0</b> for a Memo result, whose value lives on long-value pages; <b>509</b> for Text
    /// whatever size was asked for; <b>510</b> for Binary; and <b>39</b> for everything else, GUID included.
    /// None is a payload ceiling — the requested size is discarded and ACE does not truncate — and nothing
    /// moves them: not the requested size, not the length of the expression.</summary>
    internal static int CalculatedLength(JetDataType resultType) => resultType switch
    {
        JetDataType.Memo => 0,
        JetDataType.Text => 509,
        JetDataType.Binary => 510,
        _ => 39,
    };
}