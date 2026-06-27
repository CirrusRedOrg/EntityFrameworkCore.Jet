namespace LibRed.EntityFrameworkCore;

/// <summary>
/// Placeholder for the EF Core provider built on the native LibRed engine
/// (<c>UseLibRed(...)</c> options extension, type mappings, SQL generation, migrations).
/// </summary>
/// <remarks>
/// The existing <c>EntityFrameworkCore.Jet</c> provider targets ODBC/OleDb and is the
/// reference for the EF Core surface to mirror here, but this provider will sit on
/// <see cref="LibRed.Data.LibRedConnection"/> instead, making it cross-platform.
/// </remarks>
public static class LibRedProviderInfo
{
    /// <summary>Invariant name suitable for ADO.NET provider registration.</summary>
    public const string InvariantName = "LibRed.Data";
}
