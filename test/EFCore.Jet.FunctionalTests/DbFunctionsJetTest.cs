using System;
using Microsoft.EntityFrameworkCore.Query;
using Xunit;
using Xunit.Abstractions;

namespace EntityFramework.Jet.FunctionalTests
{
    public class DbFunctionsJetTest : DbFunctionsTestBase<NorthwindQueryJetFixture>
    {
        public DbFunctionsJetTest(NorthwindQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            fixture.TestSqlLoggerFactory.Clear();
        }

        public override void String_Like_Literal()
        {
            base.String_Like_Literal();

            Assert.Equal(
                @"SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE [c].[ContactName] LIKE N'%M%'",
                Sql);
        }

        public override void String_Like_Identity()
        {
            base.String_Like_Identity();

            Assert.Equal(
                @"SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE [c].[ContactName] LIKE [c].[ContactName]",
                Sql);
        }

        public override void String_Like_Literal_With_Escape()
        {
            base.String_Like_Literal_With_Escape();

            Assert.Equal(
                @"SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE [c].[ContactName] LIKE N'!%' ESCAPE N'!'",
                Sql);
        }

        private const string FileLineEnding = @"
";

        private string Sql => Fixture.TestSqlLoggerFactory.Sql.Replace(Environment.NewLine, FileLineEnding);
    }
}