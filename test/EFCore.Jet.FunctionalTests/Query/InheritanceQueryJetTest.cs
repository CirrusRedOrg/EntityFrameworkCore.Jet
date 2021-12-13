// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestModels.InheritanceModel;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class InheritanceQueryJetTest : InheritanceRelationalQueryTestBase<InheritanceQueryJetFixture>
    {
#pragma warning disable IDE0060 // Remove unused parameter
        public InheritanceQueryJetTest(InheritanceQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
#pragma warning restore IDE0060 // Remove unused parameter
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        [ConditionalFact]
        public virtual void Common_property_shares_column()
        {
            using (var context = CreateContext())
            {
                var liltType = context.Model.FindEntityType(typeof(Lilt));
                var cokeType = context.Model.FindEntityType(typeof(Coke));
                var teaType = context.Model.FindEntityType(typeof(Tea));

                Assert.Equal("SugarGrams", cokeType.FindProperty("SugarGrams").GetColumnBaseName());
                Assert.Equal("CaffeineGrams", cokeType.FindProperty("CaffeineGrams").GetColumnBaseName());
                Assert.Equal("CokeCO2", cokeType.FindProperty("Carbonation").GetColumnBaseName());

                Assert.Equal("SugarGrams", liltType.FindProperty("SugarGrams").GetColumnBaseName());
                Assert.Equal("LiltCO2", liltType.FindProperty("Carbonation").GetColumnBaseName());

                Assert.Equal("CaffeineGrams", teaType.FindProperty("CaffeineGrams").GetColumnBaseName());
                Assert.Equal("HasMilk", teaType.FindProperty("HasMilk").GetColumnBaseName());
            }
        }

        public override async Task Can_query_when_shared_column(bool async)
        {
            await base.Can_query_when_shared_column(async);

            AssertSql(
                $@"SELECT TOP 2 `d`.`Id`, `d`.`Discriminator`, `d`.`CaffeineGrams`, `d`.`CokeCO2`, `d`.`SugarGrams`
FROM `Drink` AS `d`
WHERE `d`.`Discriminator` = 'Coke'",
                //
                $@"SELECT TOP 2 `d`.`Id`, `d`.`Discriminator`, `d`.`LiltCO2`, `d`.`SugarGrams`
FROM `Drink` AS `d`
WHERE `d`.`Discriminator` = 'Lilt'",
                //
                $@"SELECT TOP 2 `d`.`Id`, `d`.`Discriminator`, `d`.`CaffeineGrams`, `d`.`HasMilk`
FROM `Drink` AS `d`
WHERE `d`.`Discriminator` = 'Tea'");
        }

        public override void FromSql_on_root()
        {
            base.FromSql_on_root();

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM (
    select * from ""Animal""
) AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi')");
        }

        public override void FromSql_on_derived()
        {
            base.FromSql_on_derived();

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`
FROM (
    select * from ""Animal""
) AS `a`
WHERE `a`.`Discriminator` = 'Eagle'");
        }

        public override async Task Can_query_all_types_when_shared_column(bool async)
        {
            await base.Can_query_all_types_when_shared_column(async);

            AssertSql(
                $@"SELECT `d`.`Id`, `d`.`Discriminator`, `d`.`CaffeineGrams`, `d`.`CokeCO2`, `d`.`SugarGrams`, `d`.`LiltCO2`, `d`.`HasMilk`
FROM `Drink` AS `d`
WHERE `d`.`Discriminator` IN ('Drink', 'Coke', 'Lilt', 'Tea')");
        }

        public override async Task Can_use_of_type_animal(bool async)
        {
            await base.Can_use_of_type_animal(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi')
ORDER BY `a`.`Species`");
        }

        public override async Task Can_use_is_kiwi(bool async)
        {
            await base.Can_use_is_kiwi(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`Discriminator` = 'Kiwi')");
        }

        public override async Task Can_use_is_kiwi_with_other_predicate(bool async)
        {
            await base.Can_use_is_kiwi_with_other_predicate(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi') AND ((`a`.`Discriminator` = 'Kiwi') AND (`a`.`CountryId` = 1))");
        }

        public override async Task Can_use_is_kiwi_in_projection(bool async)
        {
            await base.Can_use_is_kiwi_in_projection(async);

            AssertSql(
                $@"SELECT IIF(`a`.`Discriminator` = 'Kiwi', 1, 0)
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi')");
        }

        public override async Task Can_use_of_type_bird(bool async)
        {
            await base.Can_use_of_type_bird(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi') AND `a`.`Discriminator` IN ('Eagle', 'Kiwi')
ORDER BY `a`.`Species`");
        }

        public override async Task Can_use_of_type_bird_predicate(bool async)
        {
            await base.Can_use_of_type_bird_predicate(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE (`a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`CountryId` = 1)) AND `a`.`Discriminator` IN ('Eagle', 'Kiwi')
ORDER BY `a`.`Species`");
        }

        public override async Task Can_use_of_type_bird_with_projection(bool async)
        {
            await base.Can_use_of_type_bird_with_projection(async);

            AssertSql(
                $@"SELECT `a`.`EagleId`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi') AND `a`.`Discriminator` IN ('Eagle', 'Kiwi')");
        }

        public override async Task Can_use_of_type_bird_first(bool async)
        {
            await base.Can_use_of_type_bird_first(async);

            AssertSql(
                $@"SELECT TOP 1 `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi') AND `a`.`Discriminator` IN ('Eagle', 'Kiwi')
ORDER BY `a`.`Species`");
        }

        public override async Task Can_use_of_type_kiwi(bool async)
        {
            await base.Can_use_of_type_kiwi(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`Discriminator` = 'Kiwi')");
        }

        public override async Task Can_use_of_type_rose(bool async)
        {
            await base.Can_use_of_type_rose(async);

            AssertSql(
                $@"SELECT `p`.`Species`, `p`.`CountryId`, `p`.`Genus`, `p`.`Name`, `p`.`HasThorns`
FROM `Plant` AS `p`
WHERE `p`.`Genus` IN (1, 0) AND (`p`.`Genus` = 0)");
        }

        public override async Task Can_query_all_animals(bool async)
        {
            await base.Can_query_all_animals(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi')
ORDER BY `a`.`Species`");
        }

        public override async Task Can_query_all_animal_views(bool async)
        {
            await base.Can_query_all_animal_views(async);

            AssertSql(
                $@"SELECT `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM (
    SELECT * FROM Animal
) AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi')
ORDER BY `a`.`CountryId`");
        }

        public override async Task Can_query_all_plants(bool async)
        {
            await base.Can_query_all_plants(async);

            AssertSql(
                $@"SELECT `p`.`Species`, `p`.`CountryId`, `p`.`Genus`, `p`.`Name`, `p`.`HasThorns`
FROM `Plant` AS `p`
WHERE `p`.`Genus` IN (1, 0)
ORDER BY `p`.`Species`");
        }

        public override async Task Can_filter_all_animals(bool async)
        {
            await base.Can_filter_all_animals(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`Name` = 'Great spotted kiwi')
ORDER BY `a`.`Species`");
        }

        public override async Task Can_query_all_birds(bool async)
        {
            await base.Can_query_all_birds(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi')
ORDER BY `a`.`Species`");
        }

        public override async Task Can_query_just_kiwis(bool async)
        {
            await base.Can_query_just_kiwis(async);

            AssertSql(
                $@"SELECT TOP 2 `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` = 'Kiwi'");
        }

        public override async Task Can_query_just_roses(bool async)
        {
            await base.Can_query_just_roses(async);

            AssertSql(
                $@"SELECT TOP 2 `p`.`Species`, `p`.`CountryId`, `p`.`Genus`, `p`.`Name`, `p`.`HasThorns`
FROM `Plant` AS `p`
WHERE `p`.`Genus` = 0"
            );
        }

        public override async Task Can_include_prey(bool async)
        {
            await base.Can_include_prey(async);

            AssertSql(
                $@"SELECT `t`.`Species`, `t`.`CountryId`, `t`.`Discriminator`, `t`.`Name`, `t`.`EagleId`, `t`.`IsFlightless`, `t`.`Group`, `t0`.`Species`, `t0`.`CountryId`, `t0`.`Discriminator`, `t0`.`Name`, `t0`.`EagleId`, `t0`.`IsFlightless`, `t0`.`Group`, `t0`.`FoundOn`
FROM (
    SELECT TOP 2 `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`
    FROM `Animal` AS `a`
    WHERE `a`.`Discriminator` = 'Eagle'
) AS `t`
LEFT JOIN (
    SELECT `a0`.`Species`, `a0`.`CountryId`, `a0`.`Discriminator`, `a0`.`Name`, `a0`.`EagleId`, `a0`.`IsFlightless`, `a0`.`Group`, `a0`.`FoundOn`
    FROM `Animal` AS `a0`
    WHERE `a0`.`Discriminator` IN ('Eagle', 'Kiwi')
) AS `t0` ON `t`.`Species` = `t0`.`EagleId`
ORDER BY `t`.`Species`, `t0`.`Species`");
        }

        public override async Task Can_include_animals(bool async)
        {
            await base.Can_include_animals(async);

            AssertSql(
                $@"SELECT `c`.`Id`, `c`.`Name`, `t`.`Species`, `t`.`CountryId`, `t`.`Discriminator`, `t`.`Name`, `t`.`EagleId`, `t`.`IsFlightless`, `t`.`Group`, `t`.`FoundOn`
FROM `Country` AS `c`
LEFT JOIN (
    SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
    FROM `Animal` AS `a`
    WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi')
) AS `t` ON `c`.`Id` = `t`.`CountryId`
ORDER BY `c`.`Name`, `c`.`Id`, `t`.`Species`");
        }

        public override async Task Can_use_of_type_kiwi_where_north_on_derived_property(bool async)
        {
            await base.Can_use_of_type_kiwi_where_north_on_derived_property(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE (`a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`Discriminator` = 'Kiwi')) AND (`a`.`FoundOn` = 0)");
        }

        public override async Task Can_use_of_type_kiwi_where_south_on_derived_property(bool async)
        {
            await base.Can_use_of_type_kiwi_where_south_on_derived_property(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE (`a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`Discriminator` = 'Kiwi')) AND (`a`.`FoundOn` = 1)");
        }

        public override async Task Discriminator_used_when_projection_over_derived_type(bool async)
        {
            await base.Discriminator_used_when_projection_over_derived_type(async);

            AssertSql(
                $@"SELECT `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` = 'Kiwi'");
        }

        public override async Task Discriminator_used_when_projection_over_derived_type2(bool async)
        {
            await base.Discriminator_used_when_projection_over_derived_type2(async);

            AssertSql(
                $@"SELECT `a`.`IsFlightless`, `a`.`Discriminator`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi')");
        }

        public override async Task Discriminator_used_when_projection_over_of_type(bool async)
        {
            await base.Discriminator_used_when_projection_over_of_type(async);

            AssertSql(
                $@"SELECT `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`Discriminator` = 'Kiwi')");
        }

        public override void Can_insert_update_delete()
        {
            base.Can_insert_update_delete();

            AssertSql(
                $@"SELECT TOP 2 `c`.`Id`, `c`.`Name`
FROM `Country` AS `c`
WHERE `c`.`Id` = 1",
                //
                $@"{AssertSqlHelper.Declaration("@p0='Apteryx owenii' (Nullable = false) (Size = 100)")}

{AssertSqlHelper.Declaration("@p1='1'")}

{AssertSqlHelper.Declaration("@p2='Kiwi' (Nullable = false) (Size = 4000)")}

{AssertSqlHelper.Declaration("@p3='Little spotted kiwi' (Size = 4000)")}

@p4=NULL (Size = 100)
{AssertSqlHelper.Declaration("@p5='True'")}

{AssertSqlHelper.Declaration("@p6='0' (Size = 1)")}

SET NOCOUNT ON;
INSERT INTO `Animal` (`Species`, `CountryId`, `Discriminator`, `Name`, `EagleId`, `IsFlightless`, `FoundOn`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")}, {AssertSqlHelper.Parameter("@p3")}, {AssertSqlHelper.Parameter("@p4")}, {AssertSqlHelper.Parameter("@p5")}, {AssertSqlHelper.Parameter("@p6")});",
                //
                $@"SELECT TOP 2 `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE (`a`.`Discriminator` = 'Kiwi') AND (`a`.`Species` LIKE '%owenii')",
                //
                $@"{AssertSqlHelper.Declaration("@p1='Apteryx owenii' (Nullable = false) (Size = 100)")}

{AssertSqlHelper.Declaration("@p0='Aquila chrysaetos canadensis' (Size = 100)")}

SET NOCOUNT ON;
UPDATE `Animal` SET `EagleId` = {AssertSqlHelper.Parameter("@p0")}
WHERE `Species` = {AssertSqlHelper.Parameter("@p1")};
SELECT @@ROWCOUNT;",
                //
                $@"SELECT TOP 2 `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE (`a`.`Discriminator` = 'Kiwi') AND (`a`.`Species` LIKE '%owenii')",
                //
                $@"{AssertSqlHelper.Declaration("@p0='Apteryx owenii' (Nullable = false) (Size = 100)")}

SET NOCOUNT ON;
DELETE FROM `Animal`
WHERE `Species` = {AssertSqlHelper.Parameter("@p0")};
SELECT @@ROWCOUNT;",
                //
                $@"SELECT COUNT(*)
FROM `Animal` AS `a`
WHERE (`a`.`Discriminator` = 'Kiwi') AND (`a`.`Species` LIKE '%owenii')");
        }

        public override async Task Byte_enum_value_constant_used_in_projection(bool async)
        {
            await base.Byte_enum_value_constant_used_in_projection(async);

            AssertSql(
                $@"SELECT CASE
    WHEN `a`.`IsFlightless` = True THEN 0
    ELSE 1
END
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` = 'Kiwi'");
        }

        public override async Task Union_siblings_with_duplicate_property_in_subquery(bool async)
        {
            await base.Union_siblings_with_duplicate_property_in_subquery(async);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`Discriminator`, `t`.`CaffeineGrams`, `t`.`CokeCO2`, `t`.`SugarGrams`, `t`.`Carbonation`, `t`.`SugarGrams0`, `t`.`CaffeineGrams0`, `t`.`HasMilk`
FROM (
    SELECT `d`.`Id`, `d`.`Discriminator`, `d`.`CaffeineGrams`, `d`.`CokeCO2`, `d`.`SugarGrams`, NULL AS `CaffeineGrams0`, NULL AS `HasMilk`, NULL AS `Carbonation`, NULL AS `SugarGrams0`
    FROM `Drink` AS `d`
    WHERE `d`.`Discriminator` = 'Coke'
    UNION
    SELECT `d0`.`Id`, `d0`.`Discriminator`, NULL AS `CaffeineGrams`, NULL AS `CokeCO2`, NULL AS `SugarGrams`, `d0`.`CaffeineGrams` AS `CaffeineGrams0`, `d0`.`HasMilk`, NULL AS `Carbonation`, NULL AS `SugarGrams0`
    FROM `Drink` AS `d0`
    WHERE `d0`.`Discriminator` = 'Tea'
) AS `t`
WHERE `t`.`Id` > 0");
        }

        public override async Task OfType_Union_subquery(bool async)
        {
            await base.OfType_Union_subquery(async);

            AssertSql(
                $@"SELECT `t`.`Species`, `t`.`CountryId`, `t`.`Discriminator`, `t`.`Name`, `t`.`EagleId`, `t`.`IsFlightless`, `t`.`FoundOn`
FROM (
    SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`FoundOn`
    FROM `Animal` AS `a`
    WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`Discriminator` = 'Kiwi')
    UNION
    SELECT `a0`.`Species`, `a0`.`CountryId`, `a0`.`Discriminator`, `a0`.`Name`, `a0`.`EagleId`, `a0`.`IsFlightless`, `a0`.`FoundOn`
    FROM `Animal` AS `a0`
    WHERE `a0`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a0`.`Discriminator` = 'Kiwi')
) AS `t`
WHERE (`t`.`FoundOn` = 0) AND `t`.`FoundOn` IS NOT NULL");
        }

        public override async Task OfType_Union_OfType(bool async)
        {
            await base.OfType_Union_OfType(async);

            AssertSql($@" ");
        }

        public override async Task Subquery_OfType(bool async)
        {
            await base.Subquery_OfType(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT DISTINCT `t`.`Species`, `t`.`CountryId`, `t`.`Discriminator`, `t`.`Name`, `t`.`EagleId`, `t`.`IsFlightless`, `t`.`FoundOn`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
    FROM `Animal` AS `a`
    WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi')
) AS `t`
WHERE `t`.`Discriminator` = 'Kiwi'");
        }

        public override async Task Union_entity_equality(bool async)
        {
            await base.Union_entity_equality(async);

            AssertSql(
                $@"SELECT `t`.`Species`, `t`.`CountryId`, `t`.`Discriminator`, `t`.`Name`, `t`.`EagleId`, `t`.`IsFlightless`, `t`.`Group`, `t`.`FoundOn`
FROM (
    SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`FoundOn`, NULL AS `Group`
    FROM `Animal` AS `a`
    WHERE `a`.`Discriminator` = 'Kiwi'
    UNION
    SELECT `a0`.`Species`, `a0`.`CountryId`, `a0`.`Discriminator`, `a0`.`Name`, `a0`.`EagleId`, `a0`.`IsFlightless`, NULL AS `FoundOn`, `a0`.`Group`
    FROM `Animal` AS `a0`
    WHERE `a0`.`Discriminator` = 'Eagle'
) AS `t`
WHERE False = True");
        }

        protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
            => facade.UseTransaction(transaction.GetDbTransaction());

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
