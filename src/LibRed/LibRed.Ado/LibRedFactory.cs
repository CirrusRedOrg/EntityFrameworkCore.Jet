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
}
