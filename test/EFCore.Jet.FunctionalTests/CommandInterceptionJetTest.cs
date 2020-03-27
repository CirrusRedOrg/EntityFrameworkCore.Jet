// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public abstract class CommandInterceptionJetTestBase : CommandInterceptionTestBase
    {
        protected CommandInterceptionJetTestBase(InterceptionJetFixtureBase fixture)
            : base(fixture)
        {
        }

        public override async Task<string> Intercept_query_passively(bool async, bool inject)
        {
            AssertSql(
                @"SELECT `s`.`Id`, `s`.`Type` FROM `Singularity` AS `s`",
                await base.Intercept_query_passively(async, inject));

            return null;
        }

        public override async Task<string> Intercept_query_to_mutate_command(bool async, bool inject)
        {
            AssertSql(
                @"SELECT `s`.`Id`, `s`.`Type` FROM `Brane` AS `s`",
                await base.Intercept_query_to_mutate_command(async, inject));

            return null;
        }

        public override async Task<string> Intercept_query_to_replace_execution(bool async, bool inject)
        {
            AssertSql(
                @"SELECT `s`.`Id`, `s`.`Type` FROM `Singularity` AS `s`",
                await base.Intercept_query_to_replace_execution(async, inject));

            return null;
        }

        public abstract class InterceptionJetFixtureBase : InterceptionFixtureBase
        {
            protected override string StoreName => "CommandInterception";
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

            protected override IServiceCollection InjectInterceptors(
                IServiceCollection serviceCollection,
                IEnumerable<IInterceptor> injectedInterceptors)
                => base.InjectInterceptors(serviceCollection.AddEntityFrameworkJet(), injectedInterceptors);
        }

        public class CommandInterceptionJetTest
            : CommandInterceptionJetTestBase, IClassFixture<CommandInterceptionJetTest.InterceptionJetFixture>
        {
            public CommandInterceptionJetTest(InterceptionJetFixture fixture)
                : base(fixture)
            {
            }

            public class InterceptionJetFixture : InterceptionJetFixtureBase
            {
                protected override bool ShouldSubscribeToDiagnosticListener => false;

                public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
                {
                    new JetDbContextOptionsBuilder(base.AddOptions(builder))
                        .ExecutionStrategy(d => new JetExecutionStrategy(d));
                    return builder;
                }
            }
        }

        public class CommandInterceptionWithDiagnosticsJetTest
            : CommandInterceptionJetTestBase,
                IClassFixture<CommandInterceptionWithDiagnosticsJetTest.InterceptionJetFixture>
        {
            public CommandInterceptionWithDiagnosticsJetTest(InterceptionJetFixture fixture)
                : base(fixture)
            {
            }

            public class InterceptionJetFixture : InterceptionJetFixtureBase
            {
                protected override bool ShouldSubscribeToDiagnosticListener => true;

                public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
                {
                    new JetDbContextOptionsBuilder(base.AddOptions(builder))
                        .ExecutionStrategy(d => new JetExecutionStrategy(d));
                    return builder;
                }
            }
        }
    }
}
