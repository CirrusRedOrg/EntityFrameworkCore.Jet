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
        // ILibRedRelationalConnection is the concrete registration LibRed owns; IJetRelationalConnection
        // (and therefore IRelationalConnection, which Jet forwards to it) resolve to the same instance.
        // Registering on the LibRed interface — not IJetRelationalConnection — lets tests and downstream
        // code override "the LibRed connection" without naming a Jet type.
        serviceCollection.AddScoped<ILibRedRelationalConnection, LibRedRelationalConnection>();
        serviceCollection.AddScoped<IJetRelationalConnection>(p => p.GetRequiredService<ILibRedRelationalConnection>());
        // Answer existence / has-tables from LibRed's catalog instead of INFORMATION_SCHEMA + ADOX.
        serviceCollection.AddScoped<IRelationalDatabaseCreator, LibRedDatabaseCreator>();
        // Substitute the driver-free `long` mapping (DbType reflects the decimal(20,0) it's stored as) for
        // EFCore.Jet's, which only reports Decimal via an OLE DB/ODBC reflection poke a native engine can't use.
        serviceCollection.AddSingleton<IRelationalTypeMappingSource, LibRedTypeMappingSource>();
        serviceCollection.AddScoped<IExecutionStrategyFactory, LibRedExecutionStrategyFactory>();
        return serviceCollection;
    }
}
