// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.LibRed.FunctionalTests
{
    public abstract class TransactionInterceptionLibRedTestBase(
        TransactionInterceptionLibRedTestBase.InterceptionLibRedFixtureBase fixture)
        : TransactionInterceptionTestBase(fixture)
    {
        [Theory(Skip = "LibRed does not support savepoints")]
        [InlineData(true)]
        [InlineData(false)]
        public override Task Intercept_CreateSavepoint(bool async)
        {
            return base.Intercept_CreateSavepoint(async);
        }
        [Theory(Skip = "LibRed does not support savepoints")]
        [InlineData(true)]
        [InlineData(false)]
        public override Task Intercept_ReleaseSavepoint(bool async)
        {
            return base.Intercept_ReleaseSavepoint(async);
        }
        [Theory(Skip = "LibRed does not support savepoints")]
        [InlineData(true)]
        [InlineData(false)]
        public override Task Intercept_RollbackToSavepoint(bool async)
        {
            return base.Intercept_RollbackToSavepoint(async);
        }

        public abstract class InterceptionLibRedFixtureBase : InterceptionFixtureBase
        {
            protected override string StoreName => "TransactionInterception";
            protected override ITestStoreFactory TestStoreFactory => LibRedTestStoreFactory.Instance;

            protected override IServiceCollection InjectInterceptors(
                IServiceCollection serviceCollection,
                IEnumerable<IInterceptor> injectedInterceptors)
                => base.InjectInterceptors(serviceCollection.AddEntityFrameworkLibRed(), injectedInterceptors);
        }

        public class TransactionInterceptionLibRedTest(TransactionInterceptionLibRedTest.InterceptionLibRedFixture fixture)
            : TransactionInterceptionLibRedTestBase(fixture),
                IClassFixture<TransactionInterceptionLibRedTest.InterceptionLibRedFixture>
        {
            public class InterceptionLibRedFixture : InterceptionLibRedFixtureBase
            {
                protected override bool ShouldSubscribeToDiagnosticListener => false;
            }
        }

        public class TransactionInterceptionWithDiagnosticsLibRedTest(
            TransactionInterceptionWithDiagnosticsLibRedTest.InterceptionLibRedFixture fixture)
            : TransactionInterceptionLibRedTestBase(fixture),
                IClassFixture<TransactionInterceptionWithDiagnosticsLibRedTest.InterceptionLibRedFixture>
        {
            public class InterceptionLibRedFixture : InterceptionLibRedFixtureBase
            {
                protected override bool ShouldSubscribeToDiagnosticListener => true;
            }
        }
    }
}
