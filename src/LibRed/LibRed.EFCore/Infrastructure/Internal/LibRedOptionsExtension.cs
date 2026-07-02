using System.Data.Common;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.LibRed.Infrastructure.Internal;

/// <summary>
/// Options extension for the LibRed provider. It reuses EFCore.Jet's options extension wholesale
/// (so every Jet service that looks for a <see cref="JetOptionsExtension"/> still finds one) but
/// applies the LibRed service overrides instead of the plain Jet ones.
/// </summary>
public class LibRedOptionsExtension : JetOptionsExtension
{
    public LibRedOptionsExtension()
    {
    }

    protected LibRedOptionsExtension(LibRedOptionsExtension copyFrom)
        : base(copyFrom)
    {
    }

    public override void ApplyServices(IServiceCollection services)
        => services.AddEntityFrameworkLibRed();

    // Must preserve the derived type: WithConnectionString/WithConnection clone the extension,
    // and a plain JetOptionsExtension clone would silently revert ApplyServices to AddEntityFrameworkJet.
    protected override RelationalOptionsExtension Clone()
        => new LibRedOptionsExtension(this);

    // Covariant overrides: the base With...() methods clone via the (overridden) Clone() above, so the
    // returned instance is already a LibRedOptionsExtension at runtime; this just fixes the static type.
    public override LibRedOptionsExtension WithUseOuterSelectSkipEmulationViaDataReader(bool enabled)
        => (LibRedOptionsExtension)base.WithUseOuterSelectSkipEmulationViaDataReader(enabled);

    public override LibRedOptionsExtension WithEnableMillisecondsSupport(bool enabled)
        => (LibRedOptionsExtension)base.WithEnableMillisecondsSupport(enabled);

    public override LibRedOptionsExtension WithUseShortTextForSystemString(bool enabled)
        => (LibRedOptionsExtension)base.WithUseShortTextForSystemString(enabled);

    public override LibRedOptionsExtension WithDataAccessProviderFactory(DbProviderFactory dataAccessProviderFactory)
        => (LibRedOptionsExtension)base.WithDataAccessProviderFactory(dataAccessProviderFactory);
}
