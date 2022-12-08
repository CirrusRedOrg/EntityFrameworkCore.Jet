// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Query;
using System.Threading.Tasks;
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

        public override async Task Can_use_of_type_animal(bool async)
        {
            await base.Can_use_of_type_animal(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animals` AS `a`
WHERE `a`.`CountryId` = 1
ORDER BY `a`.`Species`");
        }

        public override async Task Can_use_is_kiwi(bool async)
        {
            await base.Can_use_is_kiwi(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animals` AS `a`
WHERE (`a`.`CountryId` = 1) AND (`a`.`Discriminator` = 'Kiwi')");
        }

        public override async Task Can_use_is_kiwi_with_other_predicate(bool async)
        {
            await base.Can_use_is_kiwi_with_other_predicate(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animals` AS `a`
WHERE (`a`.`CountryId` = 1) AND ((`a`.`Discriminator` = 'Kiwi') AND (`a`.`CountryId` = 1))");
        }

        public override async Task Can_use_is_kiwi_in_projection(bool async)
        {
            await base.Can_use_is_kiwi_in_projection(async);

            AssertSql(
                $@"SELECT IIF(`a`.`Discriminator` = 'Kiwi', TRUE, FALSE)
FROM `Animals` AS `a`
WHERE `a`.`CountryId` = 1");
        }

        public override async Task Can_use_of_type_bird(bool async)
        {
            await base.Can_use_of_type_bird(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animals` AS `a`
WHERE `a`.`CountryId` = 1
ORDER BY `a`.`Species`");
        }

        public override async Task Can_use_of_type_bird_predicate(bool async)
        {
            await base.Can_use_of_type_bird_predicate(async);

            AssertSql(
                $@"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animals` AS `a`
WHERE (`a`.`CountryId` = 1) AND (`a`.`CountryId` = 1)
ORDER BY `a`.`Species`");
        }

        public override async Task Can_use_of_type_bird_with_projection(bool async)
        {
            await base.Can_use_of_type_bird_with_projection(async);

            AssertSql(
                @"SELECT `a`.`EagleId`
FROM `Animals` AS `a`
WHERE `a`.`CountryId` = 1");
        }

        public override async Task Can_use_of_type_bird_first(bool async)
        {
            await base.Can_use_of_type_bird_first(async);

            AssertSql(
                @"SELECT TOP 1 `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`Group`, `a`.`FoundOn`
FROM `Animals` AS `a`
WHERE `a`.`CountryId` = 1
ORDER BY `a`.`Species`");
        }

        public override async Task Can_use_of_type_kiwi(bool async)
        {
            await base.Can_use_of_type_kiwi(async);

            AssertSql(
                @"SELECT `a`.`Species`, `a`.`CountryId`, `a`.`Discriminator`, `a`.`Name`, `a`.`EagleId`, `a`.`IsFlightless`, `a`.`FoundOn`
FROM `Animals` AS `a`
WHERE (`a`.`CountryId` = 1) AND (`a`.`Discriminator` = 'Kiwi')");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
