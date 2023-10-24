// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;
using EntityFrameworkCore.Jet.Design.Internal;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using EntityFrameworkCore.Jet.Diagnostics.Internal;
using EntityFrameworkCore.Jet.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public class JetDatabaseCleaner : RelationalDatabaseCleaner
    {
        protected override IDatabaseModelFactory CreateDatabaseModelFactory(ILoggerFactory loggerFactory)
        {
            var services = new ServiceCollection();
            services.AddEntityFrameworkJet();

            new JetDesignTimeServices().ConfigureDesignTimeServices(services);

            return services
                .BuildServiceProvider() // No scope validation; cleaner violates scopes, but only resolve services once.
                .GetRequiredService<IDatabaseModelFactory>();
        }
    }
}
