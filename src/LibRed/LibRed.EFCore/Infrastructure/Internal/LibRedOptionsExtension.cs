using EntityFrameworkCore.Jet.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LibRed.EntityFrameworkCore.Infrastructure.Internal;

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
}
