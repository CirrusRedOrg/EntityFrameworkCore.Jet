using System;
using System.Linq;
using EntityFramework.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.FunkyDataModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace EntityFramework.Jet.FunctionalTests
{
    public class FunkyDataQueryJetTest : FunkyDataQueryTestBase<FunkyDataQueryJetTest.FunkyDataQueryJetFixture>
    {
        public FunkyDataQueryJetTest(FunkyDataQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            fixture.TestSqlLoggerFactory.Clear();
        }


        [Fact(Skip = "Unsupported by JET: nullable bit not supported")]
        public override void String_ends_with_inside_conditional() {}
        [Fact(Skip = "Unsupported by JET: nullable bit not supported")]
        public override void String_ends_with_inside_conditional_negated() {}
        [Fact(Skip = "Unsupported by JET: nullable bit not supported")]
        public override void String_ends_with_on_argument_with_wildcard_column() {}
        [Fact(Skip = "Unsupported by JET: nullable bit not supported")]
        public override void String_ends_with_on_argument_with_wildcard_column_negated() {}
        [Fact(Skip = "Unsupported by JET: nullable bit not supported")]
        public override void String_ends_with_on_argument_with_wildcard_constant() {}
        [Fact(Skip = "Unsupported by JET: nullable bit not supported")]
        public override void String_ends_with_on_argument_with_wildcard_parameter() {}
        [Fact(Skip = "Unsupported by JET: nullable bit not supported")]
        public override void String_starts_with_on_argument_with_wildcard_column() {}
        [Fact(Skip = "Unsupported by JET: nullable bit not supported")]
        public override void String_starts_with_on_argument_with_wildcard_column_negated() {}
        [Fact(Skip = "Unsupported by JET: nullable bit not supported")]
        public override void String_starts_with_on_argument_with_wildcard_parameter() {}

        [Fact(Skip = "Unsupported by JET: nullable bit not supported")]
        public override void String_starts_with_on_argument_with_wildcard_constant()
        {
            using (var ctx = CreateContext())
            {
                var result1 = ctx.FunkyCustomers.Where(c => c.FirstName.EndsWith("%B")).Select(c => c.FirstName).ToList();
                var expected1 = ctx.FunkyCustomers.Select(c => c.FirstName).ToList().Where(c => c != null && c.EndsWith("%B"));
                Assert.True(expected1.Count() == result1.Count);

                var result2 = ctx.FunkyCustomers.Where(c => c.FirstName.EndsWith("_r")).Select(c => c.FirstName).ToList();
                var expected2 = ctx.FunkyCustomers.Select(c => c.FirstName).ToList().Where(c => c != null && c.EndsWith("_r"));
                Assert.True(expected2.Count() == result2.Count);

                var result3 = ctx.FunkyCustomers.Where(c => c.FirstName.EndsWith(null)).Select(c => c.FirstName).ToList();
                Assert.True(0 == result3.Count);

                var result4 = ctx.FunkyCustomers.Where(c => c.FirstName.EndsWith("")).Select(c => c.FirstName).ToList();
                Assert.True(ctx.FunkyCustomers.Count() == result4.Count);

                var result5 = ctx.FunkyCustomers.Where(c => c.FirstName.EndsWith("a__r_")).Select(c => c.FirstName).ToList();
                var expected5 = ctx.FunkyCustomers.Select(c => c.FirstName).ToList().Where(c => c != null && c.EndsWith("a__r_"));
                Assert.True(expected5.Count() == result5.Count);

                var result6 = ctx.FunkyCustomers.Where(c => !c.FirstName.EndsWith("%B%a%r")).Select(c => c.FirstName).ToList();
                var expected6 = ctx.FunkyCustomers.Select(c => c.FirstName).ToList().Where(c => c != null && !c.EndsWith("%B%a%r"));
                Assert.True(expected6.Count() == result6.Count);

                var result7 = ctx.FunkyCustomers.Where(c => !c.FirstName.EndsWith("")).Select(c => c.FirstName).ToList();
                Assert.True(0 == result7.Count);

                var result8 = ctx.FunkyCustomers.Where(c => !c.FirstName.EndsWith(null)).Select(c => c.FirstName).ToList();
                Assert.True(0 == result8.Count);
            }
        }

        [Fact(Skip = "Unsupported by JET: nullable bit not supported")]
        public override void String_ends_with_equals_nullable_column()
        {
            base.String_ends_with_equals_nullable_column();

            Assert.Equal(
                @"SELECT [c].[Id], [c].[FirstName], [c].[LastName], [c].[NullableBool], [c2].[Id], [c2].[FirstName], [c2].[LastName], [c2].[NullableBool]
FROM [FunkyCustomer] AS [c]
, [FunkyCustomer] AS [c2]
WHERE CASE
    WHEN (SUBSTRING([c].[FirstName], (LEN([c].[FirstName]) + 1) - LEN([c2].[LastName]), LEN([c2].[LastName])) = [c2].[LastName]) OR ([c2].[LastName] = N'')
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END = [c].[NullableBool]",
                Sql);
        }

        [Fact(Skip = "Unsupported by JET: nullable bit not supported")]
        public override void String_ends_with_not_equals_nullable_column()
        {
            base.String_ends_with_not_equals_nullable_column();

            Assert.Equal(
                @"SELECT [c].[Id], [c].[FirstName], [c].[LastName], [c].[NullableBool], [c2].[Id], [c2].[FirstName], [c2].[LastName], [c2].[NullableBool]
FROM [FunkyCustomer] AS [c]
, [FunkyCustomer] AS [c2]
WHERE (CASE
    WHEN (SUBSTRING([c].[FirstName], (LEN([c].[FirstName]) + 1) - LEN([c2].[LastName]), LEN([c2].[LastName])) = [c2].[LastName]) OR ([c2].[LastName] = N'')
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END <> [c].[NullableBool]) OR [c].[NullableBool] IS NULL",
                Sql);
        }

        protected override void ClearLog() => Fixture.TestSqlLoggerFactory.Clear();

        private const string FileLineEnding = @"
";

        private string Sql => Fixture.TestSqlLoggerFactory.Sql.Replace(Environment.NewLine, FileLineEnding);

        public class FunkyDataQueryJetFixture : FunkyDataQueryFixtureBase
        {
            public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ServiceProvider.GetRequiredService<ILoggerFactory>();

            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

            public override FunkyDataContext CreateContext()
            {
                var context = base.CreateContext();
                context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                return context;
            }
        }
    }
}
