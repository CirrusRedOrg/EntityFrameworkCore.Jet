// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.LibRed.FunctionalTests
{
    public class LibRedFixture : ServiceProviderFixtureBase
    {
        public static IServiceProvider DefaultServiceProvider { get; }
            = new ServiceCollection().AddEntityFrameworkLibRed().BuildServiceProvider();

        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ServiceProvider.GetRequiredService<ILoggerFactory>();
        protected override ITestStoreFactory TestStoreFactory => LibRedTestStoreFactory.Instance;

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder).ConfigureWarnings(
                w =>
                {
                    w.Log(JetEventId.ByteIdentityColumnWarning);
                    w.Log(JetEventId.DecimalTypeKeyWarning);
                });
    }
}
