using System;
using EntityFramework.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class MappingQueryJetTest : MappingQueryTestBase<MappingQueryJetTest.MappingQueryJetFixture>
    {
        public override void All_customers()
        {
            base.All_customers();

            Assert.Equal(
                @"SELECT [c].[CustomerID], [c].[CompanyName]
FROM [Customers] AS [c]",
                Sql);
        }

        public override void All_employees()
        {
            base.All_employees();

            Assert.Equal(
                @"SELECT [e].[EmployeeID], [e].[City]
FROM [Employees] AS [e]",
                Sql);
        }

        public override void All_orders()
        {
            base.All_orders();

            Assert.Equal(
                @"SELECT [o].[OrderID], [o].[ShipVia]
FROM [Orders] AS [o]",
                Sql);
        }

        public override void Project_nullable_enum()
        {
            base.Project_nullable_enum();

            Assert.Equal(
                @"SELECT [o].[ShipVia]
FROM [Orders] AS [o]",
                Sql);
        }

        private readonly MappingQueryJetFixture _fixture;

        public MappingQueryJetTest(MappingQueryJetFixture fixture) : base(fixture)
        {
            _fixture = fixture;
            _fixture.TestSqlLoggerFactory.Clear();
        }

        protected override DbContext CreateContext() => _fixture.CreateContext();

        private const string FileLineEnding = @"
";

        private string Sql => _fixture.TestSqlLoggerFactory.Sql.Replace(Environment.NewLine, FileLineEnding);

        public class MappingQueryJetFixture : MappingQueryFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => JetNorthwindTestStoreFactory.Instance;

            protected override string DatabaseSchema { get; } = "dbo";

            protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
            {
                base.OnModelCreating(modelBuilder, context);

                modelBuilder.Entity<MappedCustomer>(
                    e =>
                    {
                        e.Property(c => c.CompanyName2).Metadata.Jet().ColumnName = "CompanyName";
                        e.Metadata.Jet().TableName = "Customers";
                        e.Metadata.Jet().Schema = "dbo";
                    });

                modelBuilder.Entity<MappedEmployee>()
                    .Property(c => c.EmployeeID)
                    .HasColumnType("int");
            }
        }

    }
}