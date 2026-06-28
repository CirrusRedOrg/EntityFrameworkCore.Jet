using EntityFrameworkCore.Jet.Storage.Internal;
using LibRed.EntityFrameworkCore.Storage;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>LibRed-specific registration on top of the EFCore.Jet provider services.</summary>
public static class LibRedServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EFCore.Jet provider services, then overrides the LibRed-specific pieces.
    /// Today that is just the relational connection (native engine instead of ODBC/OLE DB); the
    /// SQL generator and others are inherited from EFCore.Jet for now. The later registration
    /// wins when a single service is resolved, so this overrides the Jet connection.
    /// </summary>
    public static IServiceCollection AddEntityFrameworkLibRed(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddEntityFrameworkJet();
        serviceCollection.AddScoped<IJetRelationalConnection, LibRedRelationalConnection>();
        return serviceCollection;
    }
}
