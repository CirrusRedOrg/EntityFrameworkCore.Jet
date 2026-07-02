using System.Data.Common;

namespace LibRed.Data;

/// <summary>
/// Connection string builder for the LibRed provider. LibRed connection strings are always
/// plain <c>Data Source=...</c> pairs, so unlike EFCore.Jet's
/// <c>DbConnectionStringBuilderExtensions.GetDataSource/SetDataSource</c> (which branch on
/// <c>DataAccessProviderType</c>/ODBC/OLE DB and throw for any other builder type), this reads
/// and writes the "Data Source" key directly - no provider-type gate to fall into.
/// </summary>
public class LibRedConnectionStringBuilder : DbConnectionStringBuilder
{
    /// <summary>
    /// Reads "Data Source", "DataSource", or "DBQ" (whichever is present - matches
    /// <see cref="LibRedConnection" />'s own tolerant key parsing), but always writes the
    /// canonical "Data Source" key.
    /// </summary>
    public string? DataSource
    {
        get => TryGetValue("Data Source", out var value)
            || TryGetValue("DataSource", out value)
            || TryGetValue("DBQ", out value)
                ? (string)value
                : null;
        set => this["Data Source"] = value;
    }
}
