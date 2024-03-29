// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;
using EF = Microsoft.EntityFrameworkCore.EF;

// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class DbFunctionsJetTest : NorthwindDbFunctionsQueryRelationalTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public DbFunctionsJetTest(
            NorthwindQueryJetFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        [ConditionalFact]
        public virtual void Check_all_tests_overridden()
            => TestHelpers.AssertAllMethodsOverridden(GetType());

        public override async Task Like_literal(bool async)
        {
            await base.Like_literal(async);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Customers` AS `c`
WHERE `c`.`ContactName` LIKE '%M%'");
        }

        public override async Task Like_identity(bool async)
        {
            await base.Like_identity(async);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Customers` AS `c`
WHERE `c`.`ContactName` LIKE `c`.`ContactName`");
        }

        [ConditionalTheory(Skip = "No escape character support in Jet")]
        public override async Task Like_literal_with_escape(bool async)
        {
            await base.Like_literal_with_escape(async);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Customers` AS `c`
WHERE `c`.`ContactName` LIKE '!%' ESCAPE '!'");
        }

        public override async Task Like_all_literals(bool async)
        {
            await base.Like_all_literals(async);

            AssertSql(
                """
SELECT COUNT(*)
FROM `Customers` AS `c`
WHERE 'FOO' LIKE '%O%'
""");
        }

        [ConditionalTheory(Skip = "No escape character support in Jet")]
        public override async Task Like_all_literals_with_escape(bool async)
        {
            await base.Like_all_literals_with_escape(async);

            AssertSql(
                """
SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE N'%' LIKE N'!%' ESCAPE N'!'
""");
        }

        [ConditionalTheory(Skip = "No ecollate support in Jet")]
        public override async Task Collate_case_insensitive(bool async)
        {
            await base.Collate_case_insensitive(async);

            AssertSql(
                """
SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE [c].[ContactName] COLLATE Latin1_General_CI_AI = N'maria anders'
""");
        }

        [ConditionalTheory(Skip = "No collate support in Jet")]
        public override async Task Collate_case_sensitive(bool async)
        {
            await base.Collate_case_sensitive(async);

            AssertSql(
                """
SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE [c].[ContactName] COLLATE Latin1_General_CS_AS = N'maria anders'
""");
        }

        [ConditionalTheory(Skip = "No collate support in Jet")]
        public override async Task Collate_case_sensitive_constant(bool async)
        {
            await base.Collate_case_sensitive_constant(async);

            AssertSql(
                """
SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE [c].[ContactName] = N'maria anders' COLLATE Latin1_General_CS_AS
""");
        }

        [ConditionalFact]
        public virtual void DateDiff_Year()
        {
            using (var context = CreateContext())
            {
                var count = context.Orders
                    .Count(c => EF.Functions.DateDiffYear(c.OrderDate, DateTime.Now) == 0);

                Assert.Equal(0, count);

                AssertSql(
                    $@"SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE DATEDIFF('yyyy', `o`.`OrderDate`, NOW()) = 0");
            }
        }

        [ConditionalFact]
        public virtual void DateDiff_Month()
        {
            using (var context = CreateContext())
            {
                var count = context.Orders
                    .Count(c => EF.Functions.DateDiffMonth(c.OrderDate, DateTime.Now) == 0);

                Assert.Equal(0, count);
                AssertSql(
                    $@"SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE DATEDIFF('m', `o`.`OrderDate`, NOW()) = 0");
            }
        }

        [ConditionalFact]
        public virtual void DateDiff_Day()
        {
            using (var context = CreateContext())
            {
                var count = context.Orders
                    .Count(c => EF.Functions.DateDiffDay(c.OrderDate, DateTime.Now) == 0);

                Assert.Equal(0, count);
                AssertSql(
                    $@"SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE DATEDIFF('d', `o`.`OrderDate`, NOW()) = 0");
            }
        }

        [ConditionalFact]
        public virtual void DateDiff_Hour()
        {
            using (var context = CreateContext())
            {
                var count = context.Orders
                    .Count(c => EF.Functions.DateDiffHour(c.OrderDate, DateTime.Now) == 0);

                Assert.Equal(0, count);
                AssertSql(
                    $@"SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE DATEDIFF('h', `o`.`OrderDate`, NOW()) = 0");
            }
        }

        [ConditionalFact]
        public virtual void DateDiff_Minute()
        {
            using (var context = CreateContext())
            {
                var count = context.Orders
                    .Count(c => EF.Functions.DateDiffMinute(c.OrderDate, DateTime.Now) == 0);

                Assert.Equal(0, count);
                AssertSql(
                    $@"SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE DATEDIFF('n', `o`.`OrderDate`, NOW()) = 0");
            }
        }

        [ConditionalFact]
        public virtual void DateDiff_Second()
        {
            using (var context = CreateContext())
            {
                var count = context.Orders
                    .Count(c => EF.Functions.DateDiffSecond(c.OrderDate, DateTime.Now) == 0);

                Assert.Equal(0, count);
                AssertSql(
                    $@"SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE DATEDIFF('s', `o`.`OrderDate`, NOW()) = 0");
            }
        }

        [ConditionalFact]
        public virtual void IsDate_not_valid()
        {
            using (var context = CreateContext())
            {
                var actual = context
                    .Orders
                    .Where(c => !EF.Functions.IsDate(c.CustomerID))
                    .Select(c => EF.Functions.IsDate(c.CustomerID))
                    .FirstOrDefault();

                Assert.False(actual);

                AssertSql(
                    $@"SELECT TOP 1 CBOOL(ISDATE(`o`.`CustomerID`))
FROM `Orders` AS `o`
WHERE CBOOL(ISDATE(`o`.`CustomerID`)) <> TRUE");
            }
        }

        [ConditionalFact]
        public virtual void IsDate_valid()
        {
            using (var context = CreateContext())
            {
                var actual = context
                    .Orders
                    .Where(c => EF.Functions.IsDate(c.OrderDate.Value.ToString()))
                    .Select(c => EF.Functions.IsDate(c.OrderDate.Value.ToString()))
                    .FirstOrDefault();

                Assert.True(actual);

                AssertSql(
                    $@"SELECT TOP 1 CBOOL(ISDATE((`o`.`OrderDate` & '')))
FROM `Orders` AS `o`
WHERE CBOOL(ISDATE((`o`.`OrderDate` & ''))) = TRUE");
            }
        }

        [ConditionalFact]
        public virtual void IsDate_join_fields()
        {
            using (var context = CreateContext())
            {
                var count = context.Orders.Count(c => EF.Functions.IsDate(c.CustomerID + c.OrderID));

                Assert.Equal(0, count);

                AssertSql(
                    $@"SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE CBOOL(ISDATE(IIF(`o`.`CustomerID` IS NULL, '', `o`.`CustomerID`) & (`o`.`OrderID` & ''))) = TRUE");
            }
        }

        [ConditionalFact]
        public void IsDate_should_throw_on_client_eval()
        {
            var exIsDate = Assert.Throws<InvalidOperationException>(() => EF.Functions.IsDate("#ISDATE#"));

            Assert.Equal(
                CoreStrings.FunctionOnClient(nameof(JetDbFunctionsExtensions.IsDate)),
                exIsDate.Message);
        }

        public override async Task Random_return_less_than_1(bool async)
        {
            await base.Random_return_less_than_1(async);

            AssertSql(
                """
SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE Rnd() < 1.0
""");
        }

        public override async Task Random_return_greater_than_0(bool async)
        {
            await base.Random_return_greater_than_0(async);

            AssertSql(
                """
SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE Rnd() >= 0.0
""");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        protected override string CaseInsensitiveCollation { get; }
        protected override string CaseSensitiveCollation { get; }
    }
}
