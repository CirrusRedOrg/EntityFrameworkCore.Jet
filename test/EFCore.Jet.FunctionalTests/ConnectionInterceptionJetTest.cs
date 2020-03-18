// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public abstract class ConnectionInterceptionJetTestBase : ConnectionInterceptionTestBase
    {
        protected ConnectionInterceptionJetTestBase(InterceptionJetFixtureBase fixture)
            : base(fixture)
        {
        }

        public abstract class InterceptionJetFixtureBase : InterceptionFixtureBase
        {
            protected override string StoreName => "ConnectionInterception";
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

            protected override IServiceCollection InjectInterceptors(
                IServiceCollection serviceCollection,
                IEnumerable<IInterceptor> injectedInterceptors)
                => base.InjectInterceptors(serviceCollection.AddEntityFrameworkJet(), injectedInterceptors);
        }

        protected override BadUniverseContext CreateBadUniverse(DbContextOptionsBuilder optionsBuilder)
            => new BadUniverseContext(optionsBuilder.UseJet(new FakeDbConnection()).Options);

        public class FakeDbConnection : DbConnection
        {
            public override string ConnectionString { get; set; }
            public override string Database => "Database";
            public override string DataSource => "DataSource";
            public override string ServerVersion => throw new NotImplementedException();
            public override ConnectionState State => ConnectionState.Closed;
            public override void ChangeDatabase(string databaseName) => throw new NotImplementedException();
            public override void Close() => throw new NotImplementedException();
            public override void Open() => throw new NotImplementedException();
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotImplementedException();
            protected override DbCommand CreateDbCommand() => throw new NotImplementedException();
        }

        public class ConnectionInterceptionJetTest
            : ConnectionInterceptionJetTestBase, IClassFixture<ConnectionInterceptionJetTest.InterceptionJetFixture>
        {
            public ConnectionInterceptionJetTest(InterceptionJetFixture fixture)
                : base(fixture)
            {
            }

            public class InterceptionJetFixture : InterceptionJetFixtureBase
            {
                protected override bool ShouldSubscribeToDiagnosticListener => false;
            }
        }

        public class ConnectionInterceptionWithDiagnosticsJetTest
            : ConnectionInterceptionJetTestBase,
                IClassFixture<ConnectionInterceptionWithDiagnosticsJetTest.InterceptionJetFixture>
        {
            public ConnectionInterceptionWithDiagnosticsJetTest(InterceptionJetFixture fixture)
                : base(fixture)
            {
            }

            public class InterceptionJetFixture : InterceptionJetFixtureBase
            {
                protected override bool ShouldSubscribeToDiagnosticListener => true;
            }
        }
    }
}
