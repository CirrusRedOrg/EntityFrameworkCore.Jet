// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using EntityFrameworkCore.LibRed.Infrastructure;
using EntityFrameworkCore.LibRed.Storage.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#nullable disable
namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests
{
    public abstract class CommandInterceptionLibRedTestBase(
        CommandInterceptionLibRedTestBase.InterceptionLibRedFixtureBase fixture)
        : CommandInterceptionTestBase(fixture)
    {
        public override async Task<string> Intercept_query_passively(bool async, bool inject)
        {
            AssertSql(
                $@"SELECT `s`.`Id`, `s`.`Type` FROM `Singularity` AS `s`",
                await base.Intercept_query_passively(async, inject));

            return null;
        }

        public override async Task<string> Intercept_query_to_mutate_command(bool async, bool inject)
        {
            AssertSql(
                $@"SELECT `s`.`Id`, `s`.`Type` FROM `Brane` AS `s`",
                await base.Intercept_query_to_mutate_command(async, inject));

            return null;
        }

        public override async Task<string> Intercept_query_to_replace_execution(bool async, bool inject)
        {
            AssertSql(
                $@"SELECT `s`.`Id`, `s`.`Type` FROM `Singularity` AS `s`",
                await base.Intercept_query_to_replace_execution(async, inject));

            return null;
        }

        public abstract class InterceptionLibRedFixtureBase : InterceptionFixtureBase
        {
            protected override string StoreName => "CommandInterception";
            protected override ITestStoreFactory TestStoreFactory => LibRedTestStoreFactory.Instance;

            protected override IServiceCollection InjectInterceptors(
                IServiceCollection serviceCollection,
                IEnumerable<IInterceptor> injectedInterceptors)
                => base.InjectInterceptors(serviceCollection.AddEntityFrameworkLibRed(), injectedInterceptors);
        }

        public class CommandInterceptionLibRedTest(CommandInterceptionLibRedTest.InterceptionLibRedFixture fixture)
            : CommandInterceptionLibRedTestBase(fixture), IClassFixture<CommandInterceptionLibRedTest.InterceptionLibRedFixture>
        {
            public class InterceptionLibRedFixture : InterceptionLibRedFixtureBase
            {
                protected override bool ShouldSubscribeToDiagnosticListener => false;

                public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
                {
                    new LibRedDbContextOptionsBuilder(base.AddOptions(builder))
                        .ExecutionStrategy(d => new LibRedExecutionStrategy(d));
                    return builder;
                }
            }
        }

        public class CommandInterceptionWithDiagnosticsLibRedTest(
            CommandInterceptionWithDiagnosticsLibRedTest.InterceptionLibRedFixture fixture)
            : CommandInterceptionLibRedTestBase(fixture),
                IClassFixture<CommandInterceptionWithDiagnosticsLibRedTest.InterceptionLibRedFixture>
        {
            public class InterceptionLibRedFixture : InterceptionLibRedFixtureBase
            {
                protected override bool ShouldSubscribeToDiagnosticListener => true;

                public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
                {
                    new LibRedDbContextOptionsBuilder(base.AddOptions(builder))
                        .ExecutionStrategy(d => new LibRedExecutionStrategy(d));
                    return builder;
                }
            }
        }
    }
}
