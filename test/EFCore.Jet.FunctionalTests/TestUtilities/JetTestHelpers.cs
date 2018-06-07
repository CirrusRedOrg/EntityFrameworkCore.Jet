using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFramework.Jet.FunctionalTests
{
    public class JetTestHelpers : TestHelpers
    {
        protected JetTestHelpers()
        {
        }

        public static JetTestHelpers Instance { get; } = new JetTestHelpers();

        public override IServiceCollection AddProviderServices(IServiceCollection services)
            => services.AddEntityFrameworkJet();

        protected override void UseProviderOptions(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseJet(new System.Data.Jet.JetConnection(ConnectionStringBuilderHelper.GetJetConnectionString("DummyDatabase")));
    }
}