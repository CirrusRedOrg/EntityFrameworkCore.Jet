// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.Design.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities
{
    public class LibRedDatabaseCleaner : RelationalDatabaseCleaner
    {
        protected override IDatabaseModelFactory CreateDatabaseModelFactory(ILoggerFactory loggerFactory)
        {
            var services = new ServiceCollection();
            services.AddEntityFrameworkLibRed();

            new LibRedDesignTimeServices().ConfigureDesignTimeServices(services);

            return services
                .BuildServiceProvider() // No scope validation; cleaner violates scopes, but only resolve services once.
                .GetRequiredService<IDatabaseModelFactory>();
        }
    }
}
