// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Query;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class FiltersInheritanceQueryJetTest : FiltersInheritanceQueryTestBase<FiltersInheritanceQueryJetFixture>
    {
        public FiltersInheritanceQueryJetTest(FiltersInheritanceQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override void Can_use_of_type_animal()
        {
            base.Can_use_of_type_animal();

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`CountryId` = 1)
ORDER BY `a`.`Species`");
        }

        public override void Can_use_is_kiwi()
        {
            base.Can_use_is_kiwi();

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE (`a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`CountryId` = 1)) AND (`a`.`Discriminator` = 'Kiwi')");
        }

        public override void Can_use_is_kiwi_with_other_predicate()
        {
            base.Can_use_is_kiwi_with_other_predicate();

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE (`a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`CountryId` = 1)) AND ((`a`.`Discriminator` = 'Kiwi') AND (`a`.`CountryId` = 1))");
        }

        public override void Can_use_is_kiwi_in_projection()
        {
            base.Can_use_is_kiwi_in_projection();

            AssertSql(
                $@"SELECT IIF(`a`.`Discriminator` = 'Kiwi', 1, 0)
FROM `Animal` AS `a`
WHERE `a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`CountryId` = 1)");
        }

        public override void Can_use_of_type_bird()
        {
            base.Can_use_of_type_bird();

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE (`a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`CountryId` = 1)) AND `a`.`Discriminator` IN ('Eagle', 'Kiwi')
ORDER BY `a`.`Species`");
        }

        public override void Can_use_of_type_bird_predicate()
        {
            base.Can_use_of_type_bird_predicate();

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE ((`a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`CountryId` = 1)) AND (`a`.`CountryId` = 1)) AND `a`.`Discriminator` IN ('Eagle', 'Kiwi')
ORDER BY `a`.`Species`");
        }

        public override void Can_use_of_type_bird_with_projection()
        {
            base.Can_use_of_type_bird_with_projection();

            AssertSql(
                $@"SELECT `a`.`EagleId`
FROM `Animal` AS `a`
WHERE (`a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`CountryId` = 1)) AND `a`.`Discriminator` IN ('Eagle', 'Kiwi')");
        }

        public override void Can_use_of_type_bird_first()
        {
            base.Can_use_of_type_bird_first();

            AssertSql(
                $@"SELECT TOP 1 `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE (`a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`CountryId` = 1)) AND `a`.`Discriminator` IN ('Eagle', 'Kiwi')
ORDER BY `a`.`Species`");
        }

        public override void Can_use_of_type_kiwi()
        {
            base.Can_use_of_type_kiwi();

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`FoundOn`
FROM `Animal` AS `a`
WHERE (`a`.`Discriminator` IN ('Eagle', 'Kiwi') AND (`a`.`CountryId` = 1)) AND (`a`.`Discriminator` = 'Kiwi')");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
