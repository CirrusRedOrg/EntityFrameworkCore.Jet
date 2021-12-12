// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using EntityFrameworkCore.Jet.Diagnostics.Internal;
using EntityFrameworkCore.Jet.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public class JetDatabaseCleaner : RelationalDatabaseCleaner
    {
        protected override IDatabaseModelFactory CreateDatabaseModelFactory(ILoggerFactory loggerFactory)
            => new JetDatabaseModelFactory(
                new DiagnosticsLogger<DbLoggerCategory.Scaffolding>(
                    loggerFactory,
                    new LoggingOptions(),
                    new DiagnosticListener("Fake"),
                    new JetLoggingDefinitions(),
                    new NullDbContextLogger()));
    }
}
