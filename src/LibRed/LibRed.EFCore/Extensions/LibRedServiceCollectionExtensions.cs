using EntityFrameworkCore.Jet.Storage.Internal;
using EntityFrameworkCore.LibRed.Storage.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        //
        // This MUST go through the EF services builder (not serviceCollection.AddSingleton) so it lands in
        // EF Core's internal service provider as a *per-options* singleton — one instance per unique
        // DbContextOptions, each built with that context's own IJetOptions. A plain application singleton is
        // constructed once with whichever context resolves it first and then shared across every context,
        // baking in the wrong UseShortTextForSystemString (the model-building context, False, wins over the
        // store context, True) — which made unbounded strings scaffold/migrate as `longchar` instead of
        // `varchar(255)`. TryAdd won't replace Jet's existing registration, so drop it first — but drop ONLY
        // EFCore.Jet's own JetTypeMappingSource, NOT a custom source a fixture/downstream registered before us.
        // (RemoveAll<IRelationalTypeMappingSource> wiped those too: e.g. the EverythingIsStrings/EverythingIsBytes
        // fixtures register their own all-string/all-byte IRelationalTypeMappingSource, then call AddProviderServices
        // → us; when one is present Jet's TryAdd already no-op'd, so there's no Jet mapping to remove and our TryAdd
        // below correctly no-ops too, leaving the custom source in place.)
        foreach (ServiceDescriptor jetMapping in serviceCollection.Where(d =>
                     d.ServiceType == typeof(IRelationalTypeMappingSource) &&
                     d.ImplementationType == typeof(JetTypeMappingSource)).ToList())
            serviceCollection.Remove(jetMapping);
        new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<IRelationalTypeMappingSource, LibRedTypeMappingSource>();
        serviceCollection.AddScoped<IExecutionStrategyFactory, LibRedExecutionStrategyFactory>();
        return serviceCollection;
    }
}
