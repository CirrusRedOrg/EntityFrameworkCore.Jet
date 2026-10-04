namespace LibRed.Formats;

/// <summary>
/// <c>MSysObjects.Type</c>: what kind of object a catalog row is, and so which container it sits in
/// (docs/format/system-catalog.md, <i>Object kinds</i>).
/// </summary>
public enum ObjectType : short
{
    /// <summary>A form, in the <c>Forms</c> container.</summary>
    Form = -32768,

    /// <summary>A macro, in the <c>Scripts</c> container.</summary>
    Macro = -32766,

    /// <summary>A report, in the <c>Reports</c> container.</summary>
    Report = -32764,

    /// <summary>A module, in the <c>Modules</c> container.</summary>
    Module = -32761,

    /// <summary>A row per user (<c>Admin</c>), in the <c>SysRel</c> container.</summary>
    User = -32758,

    /// <summary>A database document — <c>SummaryInfo</c>, <c>UserDefined</c>, <c>AccessLayout</c> — in the
    /// <c>Databases</c> container.</summary>
    DatabaseDocument = -32757,

    /// <summary>A local table, in the <c>Tables</c> container. Its <c>Id</c> is its TDEF page.</summary>
    Table = 1,

    /// <summary>The database object, <c>MSysDb</c>, in the <c>Databases</c> container.</summary>
    Database = 2,

    /// <summary>A container — <c>Tables</c>, <c>Databases</c>, <c>Relationships</c> and the rest — under the root,
    /// which has no row of its own.</summary>
    Container = 3,

    /// <summary>A stored query, in the <c>Tables</c> container — so it cannot share a table's name.</summary>
    Query = 5,

    /// <summary>A linked table, in the <c>Tables</c> container.</summary>
    LinkedTable = 6,

    /// <summary>A relationship, in the <c>Relationships</c> container.</summary>
    Relationship = 8,

    /// <summary>DAO's <c>SingleRecord</c> pseudo-object, in the <c>Relationships</c> container.</summary>
    SingleRecord = 9,
}