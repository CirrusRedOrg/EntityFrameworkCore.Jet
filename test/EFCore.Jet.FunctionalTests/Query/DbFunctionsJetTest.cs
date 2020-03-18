// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using EntityFrameworkCore.Jet.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;
using EF = Microsoft.EntityFrameworkCore.EF;

// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class DbFunctionsJetTest : DbFunctionsTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public DbFunctionsJetTest(
            NorthwindQueryJetFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override void Like_literal()
        {
            base.Like_literal();

            AssertSql(
                @"SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE [c].[ContactName] LIKE '%M%'");
        }

        public override void Like_identity()
        {
            base.Like_identity();

            AssertSql(
                @"SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE [c].[ContactName] LIKE [c].[ContactName]");
        }

        public override void Like_literal_with_escape()
        {
            base.Like_literal_with_escape();

            AssertSql(
                @"SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE [c].[ContactName] LIKE '!%' ESCAPE '!'");
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
                    @"SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE DATEDIFF(YEAR, [o].[OrderDate], GETDATE()) = 0");
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
                    @"SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE DATEDIFF(MONTH, [o].[OrderDate], GETDATE()) = 0");
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
                    @"SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE DATEDIFF(DAY, [o].[OrderDate], GETDATE()) = 0");
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
                    @"SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE DATEDIFF(HOUR, [o].[OrderDate], GETDATE()) = 0");
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
                    @"SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE DATEDIFF(MINUTE, [o].[OrderDate], GETDATE()) = 0");
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
                    @"SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE DATEDIFF(SECOND, [o].[OrderDate], GETDATE()) = 0");
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
                    @"SELECT TOP 1 CAST(ISDATE([o].[CustomerID]) AS bit)
FROM [Orders] AS [o]
WHERE CAST(ISDATE([o].[CustomerID]) AS bit) <> True");
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
                    @"SELECT TOP 1 CAST(ISDATE(CONVERT(VARCHAR(100), [o].[OrderDate])) AS bit)
FROM [Orders] AS [o]
WHERE CAST(ISDATE(CONVERT(VARCHAR(100), [o].[OrderDate])) AS bit) = True");
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
                    @"SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE CAST(ISDATE([o].[CustomerID] + CAST([o].[OrderID] AS nchar(5))) AS bit) = True");
            }
        }

        [ConditionalFact]
        public void IsDate_should_throw_on_client_eval()
        {
            var exIsDate = Assert.Throws<InvalidOperationException>(() => EF.Functions.IsDate("#ISDATE#"));

            Assert.Equal(
                JetStrings.FunctionOnClient(nameof(JetDbFunctionsExtensions.IsDate)),
                exIsDate.Message);
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
