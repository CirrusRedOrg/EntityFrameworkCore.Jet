using EntityFrameworkCore.Jet.Design.Internal;
using EntityFrameworkCore.LibRed.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.LibRed.Design.Internal;

/// <summary>
/// Design-time services for the LibRed provider. Reuses EFCore.Jet's design services but swaps
/// the database-model factory for LibRed's catalog-backed one, so reverse-engineering reads the
/// schema natively instead of via INFORMATION_SCHEMA/ADOX.
/// </summary>
public class LibRedDesignTimeServices : JetDesignTimeServices
{
    public override void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
    {
        base.ConfigureDesignTimeServices(serviceCollection);
        // Registered after the base Jet factory; the later registration wins on resolution.
        serviceCollection.AddSingleton<IDatabaseModelFactory, LibRedDatabaseModelFactory>();
    }
}
