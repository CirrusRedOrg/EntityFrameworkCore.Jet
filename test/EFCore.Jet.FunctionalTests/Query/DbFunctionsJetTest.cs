// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;
using EF = Microsoft.EntityFrameworkCore.EF;

#nullable disable
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

        public override async Task Least(bool async)
        {
            await base.Least(async);

            AssertSql(
                """
SELECT [o].[OrderID], [o].[ProductID], [o].[Discount], [o].[Quantity], [o].[UnitPrice]
FROM [Order Details] AS [o]
WHERE LEAST([o].[OrderID], 10251) = 10251
""");
        }

        public override async Task Greatest(bool async)
        {
            await base.Greatest(async);

            AssertSql(
                """
SELECT [o].[OrderID], [o].[ProductID], [o].[Discount], [o].[Quantity], [o].[UnitPrice]
FROM [Order Details] AS [o]
WHERE GREATEST([o].[OrderID], 10251) = 10251
""");
        }

        public override async Task Least_with_nullable_value_type(bool async)
        {
            await base.Least_with_nullable_value_type(async);

            AssertSql(
                """
SELECT [o].[OrderID], [o].[ProductID], [o].[Discount], [o].[Quantity], [o].[UnitPrice]
FROM [Order Details] AS [o]
WHERE LEAST([o].[OrderID], 10251) = 10251
""");
        }

        public override async Task Greatest_with_nullable_value_type(bool async)
        {
            await base.Greatest_with_nullable_value_type(async);

            AssertSql(
                """
SELECT [o].[OrderID], [o].[ProductID], [o].[Discount], [o].[Quantity], [o].[UnitPrice]
FROM [Order Details] AS [o]
WHERE GREATEST([o].[OrderID], 10251) = 10251
""");
        }

        public override async Task Least_with_parameter_array_is_not_supported(bool async)
        {
            await base.Least_with_parameter_array_is_not_supported(async);

            AssertSql();
        }

        public override async Task Greatest_with_parameter_array_is_not_supported(bool async)
        {
            await base.Greatest_with_parameter_array_is_not_supported(async);

            AssertSql();
        }

        [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task DateDiff_Year(bool async)
        {
            await AssertCount(
                async,
                ss => ss.Set<Order>(),
                ss => ss.Set<Order>(),
                c => EF.Functions.DateDiffYear(c.OrderDate, DateTime.Now) == 0,
                c => c.OrderDate.Value.Year - DateTime.Now.Year == 0);

            AssertSql(
                """
SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE DATEDIFF('yyyy', `o`.`OrderDate`, NOW()) = 0
""");
        }

        [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task DateDiff_Month(bool async)
        {
            var now = DateTime.Now;
            await AssertCount(
                async,
                ss => ss.Set<Order>(),
                ss => ss.Set<Order>(),
                c => EF.Functions.DateDiffMonth(c.OrderDate, DateTime.Now) == 0,
                c => c.OrderDate.Value.Year * 12 + c.OrderDate.Value.Month - (now.Year * 12 + now.Month) == 0);

            AssertSql(
                """
SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE DATEDIFF('m', `o`.`OrderDate`, NOW()) = 0
""");
        }

        [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task DateDiff_Day(bool async)
        {
            await AssertCount(
                async,
                ss => ss.Set<Order>(),
                ss => ss.Set<Order>(),
                c => EF.Functions.DateDiffDay(c.OrderDate, DateTime.Now) == 0,
                c => false);

            AssertSql(
                """
SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE DATEDIFF('d', `o`.`OrderDate`, NOW()) = 0
""");
        }

        [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task DateDiff_Hour(bool async)
        {
            await AssertCount(
                async,
                ss => ss.Set<Order>(),
                ss => ss.Set<Order>(),
                c => EF.Functions.DateDiffHour(c.OrderDate, DateTime.Now) == 0,
                c => false);

            AssertSql(
                """
SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE DATEDIFF('h', `o`.`OrderDate`, NOW()) = 0
""");
        }

        [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task DateDiff_Minute(bool async)
        {
            await AssertCount(
                async,
                ss => ss.Set<Order>(),
                ss => ss.Set<Order>(),
                c => EF.Functions.DateDiffMinute(c.OrderDate, DateTime.Now) == 0,
                c => false);

            AssertSql(
                """
SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE DATEDIFF('n', `o`.`OrderDate`, NOW()) = 0
""");
        }

        [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task DateDiff_Second(bool async)
        {
            await AssertCount(
                async,
                ss => ss.Set<Order>(),
                ss => ss.Set<Order>(),
                c => EF.Functions.DateDiffSecond(c.OrderDate, DateTime.Now) == 0,
                c => false);

            AssertSql(
                """
SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE DATEDIFF('s', `o`.`OrderDate`, NOW()) = 0
""");
        }

        /*[ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task DateDiff_Millisecond(bool async)
        {
            await AssertCount(
                async,
                ss => ss.Set<Order>(),
                ss => ss.Set<Order>(),
                c => EF.Functions.DateDiffMillisecond(DateTime.Now, DateTime.Now.AddDays(1)) == 0,
                c => false);

            AssertSql(
                """
SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE DATEDIFF(millisecond, GETDATE(), DATEADD(day, CAST(1.0E0 AS int), GETDATE())) = 0
""");
        }

        [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task DateDiff_Microsecond(bool async)
        {
            await AssertCount(
                async,
                ss => ss.Set<Order>(),
                ss => ss.Set<Order>(),
                c => EF.Functions.DateDiffMicrosecond(DateTime.Now, DateTime.Now.AddSeconds(1)) == 0,
                c => false);

            AssertSql(
                """
SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE DATEDIFF(microsecond, GETDATE(), DATEADD(second, CAST(1.0E0 AS int), GETDATE())) = 0
""");
        }

        [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task DateDiff_Nanosecond(bool async)
        {
            await AssertCount(
                async,
                ss => ss.Set<Order>(),
                ss => ss.Set<Order>(),
                c => EF.Functions.DateDiffNanosecond(DateTime.Now, DateTime.Now.AddSeconds(1)) == 0,
                c => false);

            AssertSql(
                """
SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE DATEDIFF(nanosecond, GETDATE(), DATEADD(second, CAST(1.0E0 AS int), GETDATE())) = 0
""");
        }

        [ConditionalFact]
        public virtual void DateDiff_Week_datetime()
        {
            using var context = CreateContext();
            var count = context.Orders
                .Count(
                    c => EF.Functions.DateDiffWeek(
                            c.OrderDate,
                            new DateTime(1998, 5, 6, 0, 0, 0))
                        == 5);

            Assert.Equal(16, count);

            AssertSql(
                """
SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE DATEDIFF(week, [o].[OrderDate], '1998-05-06T00:00:00.000') = 5
""");
        }

        [ConditionalFact]
        public virtual void DateDiff_Week_datetimeoffset()
        {
            using var context = CreateContext();
            var count = context.Orders
                .Count(
                    c => EF.Functions.DateDiffWeek(
                            c.OrderDate,
                            new DateTimeOffset(1998, 5, 6, 0, 0, 0, TimeSpan.Zero))
                        == 5);

            Assert.Equal(16, count);

            AssertSql(
                """
SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE DATEDIFF(week, CAST([o].[OrderDate] AS datetimeoffset), '1998-05-06T00:00:00.0000000+00:00') = 5
""");
        }

        [ConditionalFact]
        public virtual void DateDiff_Week_parameters_null()
        {
            using var context = CreateContext();
            var count = context.Orders
                .Count(
                    c => EF.Functions.DateDiffWeek(
                            null,
                            c.OrderDate)
                        == 5);

            Assert.Equal(0, count);

            AssertSql(
                """
SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE DATEDIFF(week, NULL, [o].[OrderDate]) = 5
""");
        }*/

        [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task IsDate_not_valid(bool async)
        {
            await AssertQueryScalar(
                async,
                ss => ss.Set<Order>().Where(o => !EF.Functions.IsDate(o.CustomerID)).Select(o => EF.Functions.IsDate(o.CustomerID)),
                ss => ss.Set<Order>().Select(c => false));

            AssertSql(
                """
SELECT CBOOL(ISDATE(`o`.`CustomerID`))
FROM `Orders` AS `o`
WHERE CBOOL(ISDATE(`o`.`CustomerID`)) <> TRUE
""");
        }

        [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task IsDate_valid(bool async)
        {
            await AssertQueryScalar(
                async,
                ss => ss.Set<Order>()
                    .Where(o => EF.Functions.IsDate(o.OrderDate.Value.ToString()))
                    .Select(o => EF.Functions.IsDate(o.OrderDate.Value.ToString())),
                ss => ss.Set<Order>().Select(o => true));

            AssertSql(
                """
SELECT CBOOL(ISDATE(IIF((`o`.`OrderDate` & '') IS NULL, '', (`o`.`OrderDate` & ''))))
FROM `Orders` AS `o`
WHERE CBOOL(ISDATE(IIF((`o`.`OrderDate` & '') IS NULL, '', (`o`.`OrderDate` & '')))) = TRUE
""");
        }

        [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task IsDate_join_fields(bool async)
        {
            await AssertCount(
                async,
                ss => ss.Set<Order>(),
                ss => ss.Set<Order>(),
                c => EF.Functions.IsDate(c.CustomerID + c.OrderID),
                c => false);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE CBOOL(ISDATE(IIF(`o`.`CustomerID` IS NULL, '', `o`.`CustomerID`) & (`o`.`OrderID` & ''))) = TRUE");
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

        protected override string CaseInsensitiveCollation
            => "";

        protected override string CaseSensitiveCollation
            => "";
    }
}
