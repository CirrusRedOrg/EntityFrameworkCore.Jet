// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Query;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class GearsOfWarFromSqlQueryJetTest : GearsOfWarFromSqlQueryTestBase<GearsOfWarQueryJetFixture>
    {
        public GearsOfWarFromSqlQueryJetTest(GearsOfWarQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            fixture.TestSqlLoggerFactory.Clear();
        }

        public override void From_sql_queryable_simple_columns_out_of_order()
        {
            base.From_sql_queryable_simple_columns_out_of_order();

            Assert.Equal(
                @"SELECT `Id`, `Name`, `IsAutomatic`, `AmmunitionType`, `OwnerFullName`, `SynergyWithId` FROM `Weapons` ORDER BY `Name`",
                Sql);
        }

        protected override void ClearLog() => Fixture.TestSqlLoggerFactory.Clear();

        private string Sql => Fixture.TestSqlLoggerFactory.Sql;
    }
}
