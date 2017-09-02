using System.Data.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Jet.Tests
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
            => optionsBuilder.UseJet(new JetConnection(JetConnection.GetConnectionString("DummyDatabase.accdb")));
    }
}
