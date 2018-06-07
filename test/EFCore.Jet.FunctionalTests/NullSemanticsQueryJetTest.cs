using EntityFramework.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet;
using EntityFrameworkCore.Jet.Infrastructure;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.NullSemanticsModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace EntityFramework.Jet.FunctionalTests
{
    public class NullSemanticsQueryJetTest : NullSemanticsQueryTestBase<NullSemanticsQueryJetTest.NullSemanticsQueryJetFixture>
    {
        public NullSemanticsQueryJetTest(NullSemanticsQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            fixture.TestSqlLoggerFactory.Clear();
            //fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }



        [Fact(Skip = "Unsupported by JET")]
        public override void From_sql_composed_with_relational_null_comparison()
        {
            base.From_sql_composed_with_relational_null_comparison();
        }

        public override void Compare_complex_equal_equal_equal()
        {
            base.Compare_complex_equal_equal_equal();

            AssertSql(
                @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN [e].[BoolA] = [e].[BoolB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END = CASE
    WHEN [e].[IntA] = [e].[IntB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN [e].[NullableBoolA] = [e].[BoolB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END = CASE
    WHEN [e].[IntA] = [e].[NullableIntB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN ([e].[NullableBoolA] = [e].[NullableBoolB]) OR ([e].[NullableBoolA] IS NULL AND [e].[NullableBoolB] IS NULL)
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END = CASE
    WHEN ([e].[NullableIntA] = [e].[NullableIntB]) OR ([e].[NullableIntA] IS NULL AND [e].[NullableIntB] IS NULL)
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END",
                Sql);
        }

        public override void Compare_complex_equal_not_equal_equal()
        {
            base.Compare_complex_equal_not_equal_equal();

            AssertSql(
                @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN [e].[BoolA] = [e].[BoolB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END <> CASE
    WHEN [e].[IntA] = [e].[IntB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN [e].[NullableBoolA] = [e].[BoolB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END <> CASE
    WHEN [e].[IntA] = [e].[NullableIntB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN ([e].[NullableBoolA] = [e].[NullableBoolB]) OR ([e].[NullableBoolA] IS NULL AND [e].[NullableBoolB] IS NULL)
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END <> CASE
    WHEN ([e].[NullableIntA] = [e].[NullableIntB]) OR ([e].[NullableIntA] IS NULL AND [e].[NullableIntB] IS NULL)
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END",
                Sql);
        }

        public override void Compare_complex_not_equal_equal_equal()
        {
            base.Compare_complex_not_equal_equal_equal();

            AssertSql(
                @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN [e].[BoolA] <> [e].[BoolB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END = CASE
    WHEN [e].[IntA] = [e].[IntB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN ([e].[NullableBoolA] <> [e].[BoolB]) OR [e].[NullableBoolA] IS NULL
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END = CASE
    WHEN [e].[IntA] = [e].[NullableIntB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN (([e].[NullableBoolA] <> [e].[NullableBoolB]) OR ([e].[NullableBoolA] IS NULL OR [e].[NullableBoolB] IS NULL)) AND ([e].[NullableBoolA] IS NOT NULL OR [e].[NullableBoolB] IS NOT NULL)
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END = CASE
    WHEN (([e].[NullableIntA] = [e].[NullableIntB]) AND ([e].[NullableIntA] IS NOT NULL AND [e].[NullableIntB] IS NOT NULL)) OR ([e].[NullableIntA] IS NULL AND [e].[NullableIntB] IS NULL)
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END",
            Sql);
        }

        public override void Compare_complex_not_equal_not_equal_equal()
        {
            base.Compare_complex_not_equal_not_equal_equal();

            AssertSql(@"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN [e].[BoolA] <> [e].[BoolB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END <> CASE
    WHEN [e].[IntA] = [e].[IntB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN ([e].[NullableBoolA] <> [e].[BoolB]) OR [e].[NullableBoolA] IS NULL
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END <> CASE
    WHEN [e].[IntA] = [e].[NullableIntB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN (([e].[NullableBoolA] <> [e].[NullableBoolB]) OR ([e].[NullableBoolA] IS NULL OR [e].[NullableBoolB] IS NULL)) AND ([e].[NullableBoolA] IS NOT NULL OR [e].[NullableBoolB] IS NOT NULL)
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END <> CASE
    WHEN (([e].[NullableIntA] = [e].[NullableIntB]) AND ([e].[NullableIntA] IS NOT NULL AND [e].[NullableIntB] IS NOT NULL)) OR ([e].[NullableIntA] IS NULL AND [e].[NullableIntB] IS NULL)
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END",
            Sql);
        }

        public override void Compare_complex_not_equal_equal_not_equal()
        {
            base.Compare_complex_not_equal_equal_not_equal();

            AssertSql(
                @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN [e].[BoolA] <> [e].[BoolB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END = CASE
    WHEN [e].[IntA] <> [e].[IntB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN ([e].[NullableBoolA] <> [e].[BoolB]) OR [e].[NullableBoolA] IS NULL
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END = CASE
    WHEN ([e].[IntA] <> [e].[NullableIntB]) OR [e].[NullableIntB] IS NULL
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN (([e].[NullableBoolA] <> [e].[NullableBoolB]) OR ([e].[NullableBoolA] IS NULL OR [e].[NullableBoolB] IS NULL)) AND ([e].[NullableBoolA] IS NOT NULL OR [e].[NullableBoolB] IS NOT NULL)
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END = CASE
    WHEN (([e].[NullableIntA] <> [e].[NullableIntB]) OR ([e].[NullableIntA] IS NULL OR [e].[NullableIntB] IS NULL)) AND ([e].[NullableIntA] IS NOT NULL OR [e].[NullableIntB] IS NOT NULL)
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END",
                Sql);
        }

        public override void Compare_complex_not_equal_not_equal_not_equal()
        {
            base.Compare_complex_not_equal_not_equal_not_equal();

            AssertSql(
                @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN [e].[BoolA] <> [e].[BoolB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END <> CASE
    WHEN [e].[IntA] <> [e].[IntB]
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN ([e].[NullableBoolA] <> [e].[BoolB]) OR [e].[NullableBoolA] IS NULL
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END <> CASE
    WHEN ([e].[IntA] <> [e].[NullableIntB]) OR [e].[NullableIntB] IS NULL
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END

SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE CASE
    WHEN (([e].[NullableBoolA] <> [e].[NullableBoolB]) OR ([e].[NullableBoolA] IS NULL OR [e].[NullableBoolB] IS NULL)) AND ([e].[NullableBoolA] IS NOT NULL OR [e].[NullableBoolB] IS NOT NULL)
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END <> CASE
    WHEN (([e].[NullableIntA] <> [e].[NullableIntB]) OR ([e].[NullableIntA] IS NULL OR [e].[NullableIntB] IS NULL)) AND ([e].[NullableIntA] IS NOT NULL OR [e].[NullableIntB] IS NOT NULL)
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END",
                Sql);
        }


        public override void Join_uses_database_semantics()
        {
            base.Join_uses_database_semantics();

            AssertSql(
                @"SELECT [e1].[Id] AS [Id1], [e2].[Id] AS [Id2], [e1].[NullableIntA], [e2].[NullableIntB]
FROM [NullSemanticsEntity1] AS [e1]
INNER JOIN [NullSemanticsEntity2] AS [e2] ON [e1].[NullableIntA] = [e2].[NullableIntB]",
                Sql);
        }


        public override void Where_equal_with_coalesce()
        {
            base.Where_equal_with_coalesce();

            AssertSql(
                @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE (COALESCE([e].[NullableStringA], [e].[NullableStringB]) = [e].[NullableStringC]) OR (([e].[NullableStringA] IS NULL AND [e].[NullableStringB] IS NULL) AND [e].[NullableStringC] IS NULL)",
                Sql);
        }

        public override void Where_not_equal_with_coalesce()
        {
            base.Where_not_equal_with_coalesce();

            AssertSql(
                @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE ((COALESCE([e].[NullableStringA], [e].[NullableStringB]) <> [e].[NullableStringC]) OR (([e].[NullableStringA] IS NULL AND [e].[NullableStringB] IS NULL) OR [e].[NullableStringC] IS NULL)) AND (([e].[NullableStringA] IS NOT NULL OR [e].[NullableStringB] IS NOT NULL) OR [e].[NullableStringC] IS NOT NULL)",
                Sql);
        }

        public override void Where_equal_with_coalesce_both_sides()
        {
            base.Where_equal_with_coalesce_both_sides();

            AssertSql(
                @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE COALESCE([e].[NullableStringA], [e].[NullableStringB]) = COALESCE([e].[StringA], [e].[StringB])",
                Sql);
        }

        public override void Where_not_equal_with_coalesce_both_sides()
        {
            base.Where_not_equal_with_coalesce_both_sides();

            AssertSql(
                @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE ((COALESCE([e].[NullableIntA], [e].[NullableIntB]) <> COALESCE([e].[NullableIntC], [e].[NullableIntB])) OR (([e].[NullableIntA] IS NULL AND [e].[NullableIntB] IS NULL) OR ([e].[NullableIntC] IS NULL AND [e].[NullableIntB] IS NULL))) AND (([e].[NullableIntA] IS NOT NULL OR [e].[NullableIntB] IS NOT NULL) OR ([e].[NullableIntC] IS NOT NULL OR [e].[NullableIntB] IS NOT NULL))",
                Sql);
        }

        public override void Where_equal_with_conditional()
        {
            base.Where_equal_with_conditional();

            AssertSql(
                @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE (CASE
    WHEN ([e].[NullableStringA] = [e].[NullableStringB]) OR ([e].[NullableStringA] IS NULL AND [e].[NullableStringB] IS NULL)
    THEN [e].[NullableStringA] ELSE [e].[NullableStringB]
END = [e].[NullableStringC]) OR (CASE
    WHEN ([e].[NullableStringA] = [e].[NullableStringB]) OR ([e].[NullableStringA] IS NULL AND [e].[NullableStringB] IS NULL)
    THEN [e].[NullableStringA] ELSE [e].[NullableStringB]
END IS NULL AND [e].[NullableStringC] IS NULL)",
                Sql);
        }

        public override void Where_not_equal_with_conditional()
        {
            base.Where_not_equal_with_conditional();

            AssertSql(
                @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE (([e].[NullableStringC] <> CASE
    WHEN (([e].[NullableStringA] = [e].[NullableStringB]) AND ([e].[NullableStringA] IS NOT NULL AND [e].[NullableStringB] IS NOT NULL)) OR ([e].[NullableStringA] IS NULL AND [e].[NullableStringB] IS NULL)
    THEN [e].[NullableStringA] ELSE [e].[NullableStringB]
END) OR ([e].[NullableStringC] IS NULL OR CASE
    WHEN (([e].[NullableStringA] = [e].[NullableStringB]) AND ([e].[NullableStringA] IS NOT NULL AND [e].[NullableStringB] IS NOT NULL)) OR ([e].[NullableStringA] IS NULL AND [e].[NullableStringB] IS NULL)
    THEN [e].[NullableStringA] ELSE [e].[NullableStringB]
END IS NULL)) AND ([e].[NullableStringC] IS NOT NULL OR CASE
    WHEN (([e].[NullableStringA] = [e].[NullableStringB]) AND ([e].[NullableStringA] IS NOT NULL AND [e].[NullableStringB] IS NOT NULL)) OR ([e].[NullableStringA] IS NULL AND [e].[NullableStringB] IS NULL)
    THEN [e].[NullableStringA] ELSE [e].[NullableStringB]
END IS NOT NULL)",
                Sql);
        }

        public override void Where_equal_with_conditional_non_nullable()
        {
            base.Where_equal_with_conditional_non_nullable();

            AssertSql(
                @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE ([e].[NullableStringC] <> CASE
    WHEN (([e].[NullableStringA] = [e].[NullableStringB]) AND ([e].[NullableStringA] IS NOT NULL AND [e].[NullableStringB] IS NOT NULL)) OR ([e].[NullableStringA] IS NULL AND [e].[NullableStringB] IS NULL)
    THEN [e].[StringA] ELSE [e].[StringB]
END) OR [e].[NullableStringC] IS NULL",
                Sql);
        }

        public override void Where_equal_with_and_and_contains()
        {
            base.Where_equal_with_and_and_contains();

            AssertSql(
                @"SELECT [e].[Id]
FROM [NullSemanticsEntity1] AS [e]
WHERE ((Instr(1, [e].[NullableStringB], [e].[NullableStringA], 0) > 0) OR ([e].[NullableStringB] = N'')) AND ([e].[BoolA] = 1)",
                Sql);
        }


        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertSql(expected);

        private void AssertContains(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertContains(expected);


        protected override NullSemanticsContext CreateContext(bool useRelationalNulls = false)
        {
            var options = new DbContextOptionsBuilder(Fixture.CreateOptions());
            if (useRelationalNulls)
            {
                new JetDbContextOptionsBuilder(options).UseRelationalNulls();
            }

            var context = new NullSemanticsContext(options.Options);

            context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            return context;
        }

        protected override void ClearLog()
            => Fixture.TestSqlLoggerFactory.Clear();

        private string Sql => Fixture.TestSqlLoggerFactory.Sql;

        public class NullSemanticsQueryJetFixture : NullSemanticsQueryRelationalFixture
        {
            public static readonly string DatabaseName = "NullSemanticsQueryTest";

            private readonly DbContextOptions _options;

            private readonly string _connectionString = JetTestStore.CreateConnectionString(DatabaseName);

            public NullSemanticsQueryJetFixture()
            {
                var serviceProvider = new ServiceCollection()
                    .AddEntityFrameworkJet()
                    .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                    .AddSingleton<ILoggerFactory>(TestSqlLoggerFactory)
                    .BuildServiceProvider();

                _options = new DbContextOptionsBuilder()
                    .EnableSensitiveDataLogging()
                    .UseInternalServiceProvider(serviceProvider)
                    .Options;
            }

            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
        }

    }
}
