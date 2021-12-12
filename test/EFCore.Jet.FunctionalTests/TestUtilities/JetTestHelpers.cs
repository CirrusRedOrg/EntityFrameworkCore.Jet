// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using EntityFrameworkCore.Jet.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public class JetTestHelpers : TestHelpers
    {
        protected JetTestHelpers()
        {
        }

        public static JetTestHelpers Instance { get; } = new JetTestHelpers();

        public override IServiceCollection AddProviderServices(IServiceCollection services)
            => services.AddEntityFrameworkJet();

        public override void UseProviderOptions(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseJet(new JetConnection("Database=DummyDatabase"));

        public override LoggingDefinitions LoggingDefinitions { get; } = new JetLoggingDefinitions();

        public string GetStoreName(string storeNameWithoutSuffix) => $"{storeNameWithoutSuffix}.accdb";
    }
}
