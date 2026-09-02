using System.Data.Common;

namespace LibRed.Data;

/// <summary>
/// <see cref="DbProviderFactory"/> for the LibRed provider, enabling provider-agnostic
/// ADO.NET code and registration via <c>DbProviderFactories</c>.
/// </summary>
public sealed class LibRedFactory : DbProviderFactory
{
    public static readonly LibRedFactory Instance = new();

    private LibRedFactory() { }

    public override DbConnection CreateConnection() => new LibRedConnection();
    public override DbCommand CreateCommand() => new LibRedCommand();
    public override DbParameter CreateParameter() => new LibRedParameter();

    /// <summary>
    /// Returns a <see cref="LibRedConnectionStringBuilder" />, not a bare
    /// <see cref="DbConnectionStringBuilder" />: EFCore.Jet.Data's
    /// <c>DbConnectionStringBuilderExtensions.SetDataSource</c> throws for any builder that isn't
    /// an ODBC/OLE DB builder, so LibRed needs its own type with its own "Data Source" handling
    /// rather than sharing that generic extension surface.
    /// </summary>
    public override DbConnectionStringBuilder CreateConnectionStringBuilder() => new LibRedConnectionStringBuilder();
}
