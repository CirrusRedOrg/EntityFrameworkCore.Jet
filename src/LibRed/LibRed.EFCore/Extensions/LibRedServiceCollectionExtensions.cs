using EntityFrameworkCore.Jet.Storage.Internal;
using EntityFrameworkCore.LibRed.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>LibRed-specific registration on top of the EFCore.Jet provider services.</summary>
public static class LibRedServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EFCore.Jet provider services, then overrides the LibRed-specific pieces.
    /// Today that is the relational connection (native engine instead of ODBC/OLE DB), the
    /// database creator, and the default execution strategy; the SQL generator and others are
    /// inherited from EFCore.Jet for now. The later registration wins when a single service is
    /// resolved, so this overrides the Jet defaults.
    /// </summary>
    public static IServiceCollection AddEntityFrameworkLibRed(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddEntityFrameworkJet();
        serviceCollection.AddScoped<IJetRelationalConnection, LibRedRelationalConnection>();
        // Resolve to the same LibRedRelationalConnection instance as IJetRelationalConnection above.
        serviceCollection.AddScoped<ILibRedRelationalConnection>(p => (ILibRedRelationalConnection)p.GetRequiredService<IJetRelationalConnection>());
        // Answer existence / has-tables from LibRed's catalog instead of INFORMATION_SCHEMA + ADOX.
        serviceCollection.AddScoped<IRelationalDatabaseCreator, LibRedDatabaseCreator>();
        serviceCollection.AddScoped<IExecutionStrategyFactory, LibRedExecutionStrategyFactory>();
        return serviceCollection;
    }
}
