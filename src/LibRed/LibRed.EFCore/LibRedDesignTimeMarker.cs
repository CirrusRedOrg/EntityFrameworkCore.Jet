using LibRed.Data;

namespace EntityFrameworkCore.LibRed;

/// <summary>
/// Placeholder for the EF Core provider built on the native LibRed engine
/// (<c>UseLibRed(...)</c> options extension, type mappings, SQL generation, migrations).
/// </summary>
/// <remarks>
/// The existing <c>EntityFrameworkCore.Jet</c> provider targets ODBC/OleDb and is the
/// reference for the EF Core surface to mirror here, but this provider will sit on
/// <see cref="LibRedConnection"/> instead, making it cross-platform.
/// <para>
/// <b>Registration plan.</b> EFCore.Jet wires every provider service through DI in
/// <c>JetServiceCollectionExtensions.AddEntityFrameworkJet()</c>
/// (<c>src/EFCore.Jet/Extensions/JetServiceCollectionExtensions.cs</c>), built with an
/// <c>EntityFrameworkRelationalServicesBuilder</c>. LibRed.EFCore exposes its own
/// <c>AddEntityFrameworkLibRed()</c> that <b>keeps as much of EFCore.Jet as possible</b>
/// and overrides only the LibRed-specific services on top:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>IQuerySqlGeneratorFactory</c> (today <c>JetQuerySqlGeneratorFactory</c>, which
///     creates <c>JetQuerySqlGenerator</c>) — the biggest difference. Because LibRed owns
///     both SQL generation and the engine/parser, the generator can shed the ACE-pleasing
///     contortions (parenthesised multi-way joins, comma-vs-JOIN quirks, CBOOL/CLNG/TOP-SKIP
///     gymnastics) that exist only to satisfy the OLE DB/ODBC → ACE path. Subclass/customise
///     on top of the Jet generator rather than rewrite.
///   </description></item>
///   <item><description>
///     The connection (<c>IRelationalConnection</c> / <c>IJetRelationalConnection</c>) → a
///     LibRed connection over <c>LibRed.Ado</c> instead of the OLE DB/ODBC <c>JetConnection</c>.
///   </description></item>
/// </list>
/// <para>
/// Consequence: because EF SQL generation is controlled top-to-bottom, the LibRed engine
/// only needs to accept the SQL EF actually emits — not arbitrary Jet syntax. Note when
/// implementing: the Jet builder uses <c>TryAdd</c> (add-if-absent), so confirm the override
/// ordering (register LibRed's services first, or replace afterward).
/// </para>
/// </remarks>
public static class LibRedProviderInfo
{
    /// <summary>Invariant name suitable for ADO.NET provider registration.</summary>
    public const string InvariantName = "LibRed.Data";
}
