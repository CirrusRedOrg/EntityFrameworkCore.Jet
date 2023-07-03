// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class JetFixture : ServiceProviderFixtureBase
    {
        public static IServiceProvider DefaultServiceProvider { get; }
            = new ServiceCollection().AddEntityFrameworkJet().BuildServiceProvider();

        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ServiceProvider.GetRequiredService<ILoggerFactory>();
        protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder).ConfigureWarnings(
                w =>
                {
                    w.Log(JetEventId.ByteIdentityColumnWarning);
                    w.Log(JetEventId.DecimalTypeKeyWarning);
                });
    }
}
