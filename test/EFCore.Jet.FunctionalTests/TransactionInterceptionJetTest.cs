// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public abstract class TransactionInterceptionJetTestBase(
        TransactionInterceptionJetTestBase.InterceptionJetFixtureBase fixture)
        : TransactionInterceptionTestBase(fixture)
    {
        [ConditionalTheory(Skip = "Jet does not support savepoints")]
        [InlineData(true)]
        [InlineData(false)]
        public override Task Intercept_CreateSavepoint(bool async)
        {
            return base.Intercept_CreateSavepoint(async);
        }
        [ConditionalTheory(Skip = "Jet does not support savepoints")]
        [InlineData(true)]
        [InlineData(false)]
        public override Task Intercept_ReleaseSavepoint(bool async)
        {
            return base.Intercept_ReleaseSavepoint(async);
        }
        [ConditionalTheory(Skip = "Jet does not support savepoints")]
        [InlineData(true)]
        [InlineData(false)]
        public override Task Intercept_RollbackToSavepoint(bool async)
        {
            return base.Intercept_RollbackToSavepoint(async);
        }

        public abstract class InterceptionJetFixtureBase : InterceptionFixtureBase
        {
            protected override string StoreName => "TransactionInterception";
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

            protected override IServiceCollection InjectInterceptors(
                IServiceCollection serviceCollection,
                IEnumerable<IInterceptor> injectedInterceptors)
                => base.InjectInterceptors(serviceCollection.AddEntityFrameworkJet(), injectedInterceptors);
        }

        public class TransactionInterceptionJetTest(TransactionInterceptionJetTest.InterceptionJetFixture fixture)
            : TransactionInterceptionJetTestBase(fixture),
                IClassFixture<TransactionInterceptionJetTest.InterceptionJetFixture>
        {
            public class InterceptionJetFixture : InterceptionJetFixtureBase
            {
                protected override bool ShouldSubscribeToDiagnosticListener => false;
            }
        }

        public class TransactionInterceptionWithDiagnosticsJetTest(
            TransactionInterceptionWithDiagnosticsJetTest.InterceptionJetFixture fixture)
            : TransactionInterceptionJetTestBase(fixture),
                IClassFixture<TransactionInterceptionWithDiagnosticsJetTest.InterceptionJetFixture>
        {
            public class InterceptionJetFixture : InterceptionJetFixtureBase
            {
                protected override bool ShouldSubscribeToDiagnosticListener => true;
            }
        }
    }
}
