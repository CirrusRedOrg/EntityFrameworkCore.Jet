// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public static class JetDatabaseFacadeExtensions
    {
        public static void EnsureClean(this DatabaseFacade databaseFacade)
            => databaseFacade.CreateExecutionStrategy()
                .Execute(databaseFacade, database => new JetDatabaseCleaner().Clean(database));
    }
}
