using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.Inheritance;
using Xunit;
using Xunit.Abstractions;

namespace EntityFramework.Jet.FunctionalTests
{
    public class InheritanceJetTest : InheritanceTestBase<JetTestStore, InheritanceJetFixture>
    {
        public InheritanceJetTest(InheritanceJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
        }

        [Fact]
        public virtual void Common_property_shares_column()
        {
            using (var context = CreateContext())
            {
                var liltType = context.Model.FindEntityType(typeof(Lilt));
                var cokeType = context.Model.FindEntityType(typeof(Coke));
                var teaType = context.Model.FindEntityType(typeof(Tea));

                Assert.Equal("SugarGrams", cokeType.FindProperty("SugarGrams").Relational().ColumnName);
                Assert.Equal("CaffeineGrams", cokeType.FindProperty("CaffeineGrams").Relational().ColumnName);
                Assert.Equal("CokeCO2", cokeType.FindProperty("Carbination").Relational().ColumnName);

                Assert.Equal("SugarGrams", liltType.FindProperty("SugarGrams").Relational().ColumnName);
                Assert.Equal("LiltCO2", liltType.FindProperty("Carbination").Relational().ColumnName);

                Assert.Equal("CaffeineGrams", teaType.FindProperty("CaffeineGrams").Relational().ColumnName);
                Assert.Equal("HasMilk", teaType.FindProperty("HasMilk").Relational().ColumnName);
            }
        }

        public override void Can_query_when_shared_column()
        {
            base.Can_query_when_shared_column();

            AssertSql(
                @"SELECT TOP 2 [d].[Id], [d].[Discriminator], [d].[CaffeineGrams], [d].[CokeCO2], [d].[SugarGrams]
FROM [Drink] AS [d]
WHERE [d].[Discriminator] = 'Coke'",
                //
                @"SELECT TOP 2 [d].[Id], [d].[Discriminator], [d].[LiltCO2], [d].[SugarGrams]
FROM [Drink] AS [d]
WHERE [d].[Discriminator] = 'Lilt'",
                //
                @"SELECT TOP 2 [d].[Id], [d].[Discriminator], [d].[CaffeineGrams], [d].[HasMilk]
FROM [Drink] AS [d]
WHERE [d].[Discriminator] = 'Tea'");
        }

        public override void Can_query_all_types_when_shared_column()
        {
            base.Can_query_all_types_when_shared_column();

            AssertSql(
                @"SELECT [d].[Id], [d].[Discriminator], [d].[CaffeineGrams], [d].[CokeCO2], [d].[SugarGrams], [d].[LiltCO2], [d].[HasMilk]
FROM [Drink] AS [d]
WHERE [d].[Discriminator] IN ('Tea', 'Lilt', 'Coke', 'Drink')");
        }

        public override void Can_use_of_type_animal()
        {
            base.Can_use_of_type_animal();

            AssertSql(
                @"SELECT [a].[Species], [a].[CountryId], [a].[Discriminator], [a].[Name], [a].[EagleId], [a].[IsFlightless], [a].[Group], [a].[FoundOn]
FROM [Animal] AS [a]
WHERE [a].[Discriminator] IN ('Kiwi', 'Eagle')
ORDER BY [a].[Species]");
        }

        public override void Can_use_is_kiwi()
        {
            base.Can_use_is_kiwi();

            AssertSql(
                @"SELECT [a].[Species], [a].[CountryId], [a].[Discriminator], [a].[Name], [a].[EagleId], [a].[IsFlightless], [a].[Group], [a].[FoundOn]
FROM [Animal] AS [a]
WHERE [a].[Discriminator] IN ('Kiwi', 'Eagle') AND ([a].[Discriminator] = 'Kiwi')");
        }

        public override void Can_use_is_kiwi_with_other_predicate()
        {
            base.Can_use_is_kiwi_with_other_predicate();

            AssertSql(
                @"SELECT [a].[Species], [a].[CountryId], [a].[Discriminator], [a].[Name], [a].[EagleId], [a].[IsFlightless], [a].[Group], [a].[FoundOn]
FROM [Animal] AS [a]
WHERE [a].[Discriminator] IN ('Kiwi', 'Eagle') AND (([a].[Discriminator] = 'Kiwi') AND ([a].[CountryId] = 1))");
        }

        [Fact]
        public override void Can_use_of_type_bird()
        {
            base.Can_use_of_type_bird();

            AssertSql(
                @"SELECT [a].[Species], [a].[CountryId], [a].[Discriminator], [a].[Name], [a].[EagleId], [a].[IsFlightless], [a].[Group], [a].[FoundOn]
FROM [Animal] AS [a]
WHERE [a].[Discriminator] IN ('Kiwi', 'Eagle')
ORDER BY [a].[Species]");
        }

        public override void Can_use_of_type_bird_predicate()
        {
            base.Can_use_of_type_bird_predicate();

            AssertSql(
                @"SELECT [a].[Species], [a].[CountryId], [a].[Discriminator], [a].[Name], [a].[EagleId], [a].[IsFlightless], [a].[Group], [a].[FoundOn]
FROM [Animal] AS [a]
WHERE [a].[Discriminator] IN ('Kiwi', 'Eagle') AND ([a].[CountryId] = 1)
ORDER BY [a].[Species]");
        }

        public override void Can_use_of_type_bird_with_projection()
        {
            base.Can_use_of_type_bird_with_projection();

            AssertSql(
                @"SELECT [b].[EagleId]
FROM [Animal] AS [b]
WHERE [b].[Discriminator] IN ('Kiwi', 'Eagle')");
        }

        public override void Can_use_of_type_bird_first()
        {
            base.Can_use_of_type_bird_first();

            AssertSql(
                @"SELECT TOP 1 [a].[Species], [a].[CountryId], [a].[Discriminator], [a].[Name], [a].[EagleId], [a].[IsFlightless], [a].[Group], [a].[FoundOn]
FROM [Animal] AS [a]
WHERE [a].[Discriminator] IN ('Kiwi', 'Eagle')
ORDER BY [a].[Species]");
        }

        public override void Can_use_of_type_kiwi()
        {
            base.Can_use_of_type_kiwi();

            AssertSql(
                @"SELECT [a].[Species], [a].[CountryId], [a].[Discriminator], [a].[Name], [a].[EagleId], [a].[IsFlightless], [a].[FoundOn]
FROM [Animal] AS [a]
WHERE [a].[Discriminator] = 'Kiwi'");
        }

        public override void Can_use_of_type_rose()
        {
            base.Can_use_of_type_rose();

            AssertSql(
                @"SELECT [p].[Species], [p].[CountryId], [p].[Genus], [p].[Name], [p].[HasThorns]
FROM [Plant] AS [p]
WHERE [p].[Genus] = 0");
        }

        public override void Can_query_all_animals()
        {
            base.Can_query_all_animals();

            AssertSql(
                @"SELECT [a].[Species], [a].[CountryId], [a].[Discriminator], [a].[Name], [a].[EagleId], [a].[IsFlightless], [a].[Group], [a].[FoundOn]
FROM [Animal] AS [a]
WHERE [a].[Discriminator] IN ('Kiwi', 'Eagle')
ORDER BY [a].[Species]");
        }

        public override void Can_query_all_plants()
        {
            base.Can_query_all_plants();

            AssertSql(
                @"SELECT [a].[Species], [a].[CountryId], [a].[Genus], [a].[Name], [a].[HasThorns]
FROM [Plant] AS [a]
WHERE [a].[Genus] IN (0, 1)
ORDER BY [a].[Species]");
        }

        public override void Can_filter_all_animals()
        {
            base.Can_filter_all_animals();

            AssertSql(
                @"SELECT [a].[Species], [a].[CountryId], [a].[Discriminator], [a].[Name], [a].[EagleId], [a].[IsFlightless], [a].[Group], [a].[FoundOn]
FROM [Animal] AS [a]
WHERE [a].[Discriminator] IN ('Kiwi', 'Eagle') AND ([a].[Name] = 'Great spotted kiwi')
ORDER BY [a].[Species]");
        }

        public override void Can_query_all_birds()
        {
            base.Can_query_all_birds();

            AssertSql(
                @"SELECT [a].[Species], [a].[CountryId], [a].[Discriminator], [a].[Name], [a].[EagleId], [a].[IsFlightless], [a].[Group], [a].[FoundOn]
FROM [Animal] AS [a]
WHERE [a].[Discriminator] IN ('Kiwi', 'Eagle')
ORDER BY [a].[Species]");
        }

        public override void Can_query_just_kiwis()
        {
            base.Can_query_just_kiwis();

            AssertSql(
                @"SELECT TOP 2 [a].[Species], [a].[CountryId], [a].[Discriminator], [a].[Name], [a].[EagleId], [a].[IsFlightless], [a].[FoundOn]
FROM [Animal] AS [a]
WHERE [a].[Discriminator] = 'Kiwi'");
        }

        public override void Can_query_just_roses()
        {
            base.Can_query_just_roses();

            AssertSql(
                @"SELECT TOP 2 [p].[Species], [p].[CountryId], [p].[Genus], [p].[Name], [p].[HasThorns]
FROM [Plant] AS [p]
WHERE [p].[Genus] = 0"
            );
        }

        public override void Can_use_of_type_kiwi_where_north_on_derived_property()
        {
            base.Can_use_of_type_kiwi_where_north_on_derived_property();

            AssertSql(
                @"SELECT [x].[Species], [x].[CountryId], [x].[Discriminator], [x].[Name], [x].[EagleId], [x].[IsFlightless], [x].[FoundOn]
FROM [Animal] AS [x]
WHERE ([x].[Discriminator] = 'Kiwi') AND ([x].[FoundOn] = 0)");
        }

        public override void Can_use_of_type_kiwi_where_south_on_derived_property()
        {
            base.Can_use_of_type_kiwi_where_south_on_derived_property();

            AssertSql(
                @"SELECT [x].[Species], [x].[CountryId], [x].[Discriminator], [x].[Name], [x].[EagleId], [x].[IsFlightless], [x].[FoundOn]
FROM [Animal] AS [x]
WHERE ([x].[Discriminator] = 'Kiwi') AND ([x].[FoundOn] = 1)");
        }

        public override void Discriminator_used_when_projection_over_derived_type()
        {
            base.Discriminator_used_when_projection_over_derived_type();

            AssertSql(
                @"SELECT [k].[FoundOn]
FROM [Animal] AS [k]
WHERE [k].[Discriminator] = 'Kiwi'");
        }

        [Fact]
        public override void Discriminator_used_when_projection_over_derived_type2()
        {
            base.Discriminator_used_when_projection_over_derived_type2();

            AssertSql(
                @"SELECT [b].[IsFlightless], [b].[Discriminator]
FROM [Animal] AS [b]
WHERE [b].[Discriminator] IN ('Kiwi', 'Eagle')");
        }

        [Fact]
        public override void Discriminator_used_when_projection_over_of_type()
        {
            base.Discriminator_used_when_projection_over_of_type();

            AssertSql(
                @"SELECT [k].[FoundOn]
FROM [Animal] AS [k]
WHERE [k].[Discriminator] = 'Kiwi'");
        }

        [Fact(Skip = "Investigate - https://github.com/aspnet/EntityFramework/issues/9379")]
        public override void Can_insert_update_delete()
        {
            base.Can_insert_update_delete();

            AssertSql(
                @"SELECT TOP 2 [c].[Id], [c].[Name]
FROM [Country] AS [c]
WHERE [c].[Id] = 1",
                //
                @"@p0='Apteryx owenii' (Nullable = false) (Size = 100)
@p1='1'
@p2='Kiwi' (Nullable = false) (Size = 4000)
@p3='Little spotted kiwi' (Size = 4000)
@p4='' (Size = 100) (DbType = String)
@p5='True'
@p6='North'

INSERT INTO [Animal] ([Species], [CountryId], [Discriminator], [Name], [EagleId], [IsFlightless], [FoundOn])
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6)",
                //
                @"SELECT TOP 2 [k].[Species], [k].[CountryId], [k].[Discriminator], [k].[Name], [k].[EagleId], [k].[IsFlightless], [k].[FoundOn]
FROM [Animal] AS [k]
WHERE ([k].[Discriminator] = 'Kiwi') AND (SUBSTRING([k].[Species], (LEN([k].[Species]) + 1) - LEN('owenii'), LEN('owenii')) = 'owenii')",
                //
                @"@p1='Apteryx owenii' (Nullable = false) (Size = 100)
@p0='Aquila chrysaetos canadensis' (Size = 100)

UPDATE [Animal] SET [EagleId] = @p0
WHERE [Species] = @p1",
                //
                @"SELECT TOP 2 [k].[Species], [k].[CountryId], [k].[Discriminator], [k].[Name], [k].[EagleId], [k].[IsFlightless], [k].[FoundOn]
FROM [Animal] AS [k]
WHERE ([k].[Discriminator] = 'Kiwi') AND (SUBSTRING([k].[Species], (LEN([k].[Species]) + 1) - LEN('owenii'), LEN('owenii')) = 'owenii')",
                //
                @"@p0='Apteryx owenii' (Nullable = false) (Size = 100)

DELETE FROM [Animal]
WHERE [Species] = @p0",
                //
                @"SELECT COUNT(*)
FROM [Animal] AS [k]
WHERE ([k].[Discriminator] = 'Kiwi') AND (SUBSTRING([k].[Species], (LEN([k].[Species]) + 1) - LEN('owenii'), LEN('owenii')) = 'owenii')");
        }

        private void AssertSql(params string[] expected)
        {
            string[] expectedFixed = new string[expected.Length];
            int i = 0;
            foreach (var item in expected)
            {
                expectedFixed[i++] = item.Replace("\r\n", "\n");
            }
            Fixture.TestSqlLoggerFactory.AssertBaseline(expectedFixed);
        }
    }
}