// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query
{
    public class MappingQueryLibRedTest : MappingQueryTestBase<MappingQueryLibRedTest.MappingQueryLibRedFixture>
    {
        public override void All_customers()
        {
            base.All_customers();

            AssertSql(
                $"""
                    SELECT `c`.`CustomerID`, `c`.`CompanyName`
                    FROM `Customers` AS `c`
                    """);
        }

        public override void All_employees()
        {
            base.All_employees();

            AssertSql(
                $"""
                    SELECT `e`.`EmployeeID`, `e`.`City`
                    FROM `Employees` AS `e`
                    """);
        }

        public override void All_orders()
        {
            base.All_orders();

            AssertSql(
                $"""
                    SELECT `o`.`OrderID`, `o`.`ShipVia`
                    FROM `Orders` AS `o`
                    """);
        }

        public override void Project_nullable_enum()
        {
            base.Project_nullable_enum();

            AssertSql(
                $"""
                    SELECT `o`.`ShipVia`
                    FROM `Orders` AS `o`
                    """);
        }

        public MappingQueryLibRedTest(MappingQueryLibRedFixture fixture)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        public class MappingQueryLibRedFixture : MappingQueryFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => LibRedNorthwindTestStoreFactory.Instance;

            protected override string DatabaseSchema { get; } = "dbo";

            protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
            {
                base.OnModelCreating(modelBuilder, context);

                modelBuilder.Entity<MappedCustomer>(
                    e =>
                    {
                        e.Property(c => c.CompanyName2).Metadata.SetColumnName("CompanyName");
                        e.Metadata.SetTableName("Customers");
                        e.Metadata.SetSchema(null);
                    });

                modelBuilder.Entity<MappedEmployee>()
                    .Property(c => c.EmployeeID)
                    .HasColumnType("int");
            }
        }
    }
}
