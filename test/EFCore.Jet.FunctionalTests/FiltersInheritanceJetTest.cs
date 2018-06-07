using Microsoft.EntityFrameworkCore.Query;
using Xunit.Abstractions;

namespace EntityFramework.Jet.FunctionalTests
{
    public class FiltersInheritanceJetTest : FiltersInheritanceTestBase<FiltersInheritanceJetFixture>
    {
        public FiltersInheritanceJetTest(FiltersInheritanceJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override void Can_use_of_type_animal()
        {
            base.Can_use_of_type_animal();

            AssertSql(
                @"SELECT [a].[Species], [a].[CountryId], [a].[Discriminator], [a].[Name], [a].[EagleId], [a].[IsFlightless], [a].[Group], [a].[FoundOn]
FROM [Animal] AS [a]
WHERE [a].[Discriminator] IN ('Kiwi', 'Eagle') AND ([a].[CountryId] = 1)
ORDER BY [a].[Species]");
        }

        public override void Can_use_is_kiwi()
        {
            base.Can_use_is_kiwi();

            AssertSql(
                @"SELECT [a].[Species], [a].[CountryId], [a].[Discriminator], [a].[Name], [a].[EagleId], [a].[IsFlightless], [a].[Group], [a].[FoundOn]
FROM [Animal] AS [a]
WHERE ([a].[Discriminator] IN ('Kiwi', 'Eagle') AND ([a].[CountryId] = 1)) AND ([a].[Discriminator] = 'Kiwi')");
        }

        public override void Can_use_is_kiwi_with_other_predicate()
        {
            base.Can_use_is_kiwi_with_other_predicate();

            AssertSql(
                @"SELECT [a].[Species], [a].[CountryId], [a].[Discriminator], [a].[Name], [a].[EagleId], [a].[IsFlightless], [a].[Group], [a].[FoundOn]
FROM [Animal] AS [a]
WHERE ([a].[Discriminator] IN ('Kiwi', 'Eagle') AND ([a].[CountryId] = 1)) AND (([a].[Discriminator] = 'Kiwi') AND ([a].[CountryId] = 1))");
        }


        public override void Can_use_of_type_bird()
        {
            base.Can_use_of_type_bird();

            AssertSql(
                @"SELECT [b].[Species], [b].[CountryId], [b].[Discriminator], [b].[Name], [b].[EagleId], [b].[IsFlightless], [b].[Group], [b].[FoundOn]
FROM [Animal] AS [b]
WHERE [b].[Discriminator] IN ('Kiwi', 'Eagle') AND ([b].[CountryId] = 1)
ORDER BY [b].[Species]");
        }

        public override void Can_use_of_type_bird_predicate()
        {
            base.Can_use_of_type_bird_predicate();

            AssertSql(
                @"SELECT [b].[Species], [b].[CountryId], [b].[Discriminator], [b].[Name], [b].[EagleId], [b].[IsFlightless], [b].[Group], [b].[FoundOn]
FROM [Animal] AS [b]
WHERE ([b].[Discriminator] IN ('Kiwi', 'Eagle') AND ([b].[CountryId] = 1)) AND ([b].[CountryId] = 1)
ORDER BY [b].[Species]");
        }

        public override void Can_use_of_type_bird_with_projection()
        {
            base.Can_use_of_type_bird_with_projection();

            AssertSql(
                @"SELECT [b].[EagleId]
FROM [Animal] AS [b]
WHERE [b].[Discriminator] IN ('Kiwi', 'Eagle') AND ([b].[CountryId] = 1)");
        }

        public override void Can_use_of_type_bird_first()
        {
            base.Can_use_of_type_bird_first();

            AssertSql(
                @"SELECT TOP 1 [b].[Species], [b].[CountryId], [b].[Discriminator], [b].[Name], [b].[EagleId], [b].[IsFlightless], [b].[Group], [b].[FoundOn]
FROM [Animal] AS [b]
WHERE [b].[Discriminator] IN ('Kiwi', 'Eagle') AND ([b].[CountryId] = 1)
ORDER BY [b].[Species]");
        }

        public override void Can_use_of_type_kiwi()
        {
            base.Can_use_of_type_kiwi();

            AssertSql(
                @"SELECT [k].[Species], [k].[CountryId], [k].[Discriminator], [k].[Name], [k].[EagleId], [k].[IsFlightless], [k].[FoundOn]
FROM [Animal] AS [k]
WHERE ([k].[Discriminator] = 'Kiwi') AND ([k].[CountryId] = 1)");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertSql(expected);

        private void AssertContains(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertContains(expected);

    }
}