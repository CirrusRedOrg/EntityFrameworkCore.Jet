// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

// ReSharper disable InconsistentNaming

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class UdfDbFunctionJetTests : UdfDbFunctionTestBase<UdfDbFunctionJetTests.Jet>
    {
        public UdfDbFunctionJetTests(Jet fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        #region Scalar Tests

        #region Static

        public override void Scalar_Function_Extension_Method_Static()
        {
            base.Scalar_Function_Extension_Method_Static();

            AssertSql(
                @"SELECT COUNT(*)
FROM `Customers` AS `c`
WHERE IsDate(`c`.`FirstName`) = False");
        }

        public override void Scalar_Function_With_Translator_Translates_Static()
        {
            base.Scalar_Function_With_Translator_Translates_Static();

            AssertSql(
                @"@__customerId_0='3'

SELECT TOP 2 len(`c`.`LastName`)
FROM `Customers` AS `c`
WHERE `c`.`Id` = @__customerId_0");
        }

        public override void Scalar_Function_Constant_Parameter_Static()
        {
            base.Scalar_Function_Constant_Parameter_Static();

            AssertSql(
                @"@__customerId_0='1'

SELECT `CustomerOrderCount`(@__customerId_0)
FROM `Customers` AS `c`");
        }

        public override void Scalar_Function_Anonymous_Type_Select_Correlated_Static()
        {
            base.Scalar_Function_Anonymous_Type_Select_Correlated_Static();

            AssertSql(
                @"SELECT TOP 2 `c`.`LastName`, `CustomerOrderCount`(`c`.`Id`) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = 1");
        }

        public override void Scalar_Function_Anonymous_Type_Select_Not_Correlated_Static()
        {
            base.Scalar_Function_Anonymous_Type_Select_Not_Correlated_Static();

            AssertSql(
                @"SELECT TOP 2 `c`.`LastName`, `CustomerOrderCount`(1) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = 1");
        }

        public override void Scalar_Function_Anonymous_Type_Select_Parameter_Static()
        {
            base.Scalar_Function_Anonymous_Type_Select_Parameter_Static();

            AssertSql(
                @"@__customerId_0='1'

SELECT TOP 2 `c`.`LastName`, `CustomerOrderCount`(@__customerId_0) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = @__customerId_0");
        }

        public override void Scalar_Function_Anonymous_Type_Select_Nested_Static()
        {
            base.Scalar_Function_Anonymous_Type_Select_Nested_Static();

            AssertSql(
                @"@__starCount_1='3'
@__customerId_0='3'

SELECT TOP 2 `c`.`LastName`, `StarValue`(@__starCount_1, `CustomerOrderCount`(@__customerId_0)) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = @__customerId_0");
        }

        public override void Scalar_Function_Where_Correlated_Static()
        {
            base.Scalar_Function_Where_Correlated_Static();

            AssertSql(
                @"SELECT LOWER(CONVERT(VARCHAR(11), `c`.`Id`))
FROM `Customers` AS `c`
WHERE `IsTopCustomer`(`c`.`Id`) = True");
        }

        public override void Scalar_Function_Where_Not_Correlated_Static()
        {
            base.Scalar_Function_Where_Not_Correlated_Static();

            AssertSql(
                @"@__startDate_0='2000-04-01T00:00:00' (Nullable = true)

SELECT TOP 2 `c`.`Id`
FROM `Customers` AS `c`
WHERE `GetCustomerWithMostOrdersAfterDate`(@__startDate_0) = `c`.`Id`");
        }

        public override void Scalar_Function_Where_Parameter_Static()
        {
            base.Scalar_Function_Where_Parameter_Static();

            AssertSql(
                @"@__period_0='0'

SELECT TOP 2 `c`.`Id`
FROM `Customers` AS `c`
WHERE `c`.`Id` = `GetCustomerWithMostOrdersAfterDate`(`GetReportingPeriodStartDate`(@__period_0))");
        }

        public override void Scalar_Function_Where_Nested_Static()
        {
            base.Scalar_Function_Where_Nested_Static();

            AssertSql(
                @"SELECT TOP 2 `c`.`Id`
FROM `Customers` AS `c`
WHERE `c`.`Id` = `GetCustomerWithMostOrdersAfterDate`(`GetReportingPeriodStartDate`(0))");
        }

        public override void Scalar_Function_Let_Correlated_Static()
        {
            base.Scalar_Function_Let_Correlated_Static();

            AssertSql(
                @"SELECT TOP 2 `c`.`LastName`, `CustomerOrderCount`(`c`.`Id`) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = 2");
        }

        public override void Scalar_Function_Let_Not_Correlated_Static()
        {
            base.Scalar_Function_Let_Not_Correlated_Static();

            AssertSql(
                @"SELECT TOP 2 `c`.`LastName`, `CustomerOrderCount`(2) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = 2");
        }

        public override void Scalar_Function_Let_Not_Parameter_Static()
        {
            base.Scalar_Function_Let_Not_Parameter_Static();

            AssertSql(
                @"@__customerId_0='2'

SELECT TOP 2 `c`.`LastName`, `CustomerOrderCount`(@__customerId_0) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = @__customerId_0");
        }

        public override void Scalar_Function_Let_Nested_Static()
        {
            base.Scalar_Function_Let_Nested_Static();

            AssertSql(
                @"@__starCount_0='3'
@__customerId_1='1'

SELECT TOP 2 `c`.`LastName`, `StarValue`(@__starCount_0, `CustomerOrderCount`(@__customerId_1)) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = @__customerId_1");
        }

        public override void Scalar_Nested_Function_Unwind_Client_Eval_Select_Static()
        {
            base.Scalar_Nested_Function_Unwind_Client_Eval_Select_Static();

            AssertSql(
                @"SELECT `c`.`Id`
FROM `Customers` AS `c`
ORDER BY `c`.`Id`");
        }

        public override void Scalar_Nested_Function_UDF_BCL_Static()
        {
            base.Scalar_Nested_Function_UDF_BCL_Static();

            AssertSql(
                @"SELECT TOP 2 `c`.`Id`
FROM `Customers` AS `c`
WHERE 3 = `CustomerOrderCount`(ABS(`c`.`Id`))");
        }

        public override void Nullable_navigation_property_access_preserves_schema_for_sql_function()
        {
            base.Nullable_navigation_property_access_preserves_schema_for_sql_function();

            AssertSql(
                @"SELECT TOP 1 `IdentityString`(`c`.`FirstName`)
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerId` = `c`.`Id`
ORDER BY `o`.`Id`");
        }

        public override void Scalar_Function_SqlFragment_Static()
        {
            base.Scalar_Function_SqlFragment_Static();

            AssertSql(
                @"SELECT COUNT(*)
FROM `Customers` AS `c`
WHERE `c`.`LastName` = 'Two'");
        }

        #endregion

        #region Instance

        public override void Scalar_Function_Non_Static()
        {
            base.Scalar_Function_Non_Static();

            AssertSql(
                @"SELECT TOP 2 `StarValue`(4, `c`.`Id`) AS `Id`, `DollarValue`(2, `c`.`LastName`) AS `LastName`
FROM `Customers` AS `c`
WHERE `c`.`Id` = 1");
        }

        public override void Scalar_Function_Extension_Method_Instance()
        {
            base.Scalar_Function_Extension_Method_Instance();

            AssertSql(
                @"SELECT COUNT(*)
FROM `Customers` AS `c`
WHERE IsDate(`c`.`FirstName`) = False");
        }

        public override void Scalar_Function_With_Translator_Translates_Instance()
        {
            base.Scalar_Function_With_Translator_Translates_Instance();

            AssertSql(
                @"@__customerId_0='3'

SELECT TOP 2 len(`c`.`LastName`)
FROM `Customers` AS `c`
WHERE `c`.`Id` = @__customerId_0");
        }

        public override void Scalar_Function_Constant_Parameter_Instance()
        {
            base.Scalar_Function_Constant_Parameter_Instance();

            AssertSql(
                @"@__customerId_1='1'

SELECT `CustomerOrderCount`(@__customerId_1)
FROM `Customers` AS `c`");
        }

        public override void Scalar_Function_Anonymous_Type_Select_Correlated_Instance()
        {
            base.Scalar_Function_Anonymous_Type_Select_Correlated_Instance();

            AssertSql(
                @"SELECT TOP 2 `c`.`LastName`, `CustomerOrderCount`(`c`.`Id`) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = 1");
        }

        public override void Scalar_Function_Anonymous_Type_Select_Not_Correlated_Instance()
        {
            base.Scalar_Function_Anonymous_Type_Select_Not_Correlated_Instance();

            AssertSql(
                @"SELECT TOP 2 `c`.`LastName`, `CustomerOrderCount`(1) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = 1");
        }

        public override void Scalar_Function_Anonymous_Type_Select_Parameter_Instance()
        {
            base.Scalar_Function_Anonymous_Type_Select_Parameter_Instance();

            AssertSql(
                @"@__customerId_0='1'

SELECT TOP 2 `c`.`LastName`, `CustomerOrderCount`(@__customerId_0) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = @__customerId_0");
        }

        public override void Scalar_Function_Anonymous_Type_Select_Nested_Instance()
        {
            base.Scalar_Function_Anonymous_Type_Select_Nested_Instance();

            AssertSql(
                @"@__starCount_2='3'
@__customerId_0='3'

SELECT TOP 2 `c`.`LastName`, `StarValue`(@__starCount_2, `CustomerOrderCount`(@__customerId_0)) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = @__customerId_0");
        }

        public override void Scalar_Function_Where_Correlated_Instance()
        {
            base.Scalar_Function_Where_Correlated_Instance();

            AssertSql(
                @"SELECT LOWER(CONVERT(VARCHAR(11), `c`.`Id`))
FROM `Customers` AS `c`
WHERE `IsTopCustomer`(`c`.`Id`) = True");
        }

        public override void Scalar_Function_Where_Not_Correlated_Instance()
        {
            base.Scalar_Function_Where_Not_Correlated_Instance();

            AssertSql(
                @"@__startDate_1='2000-04-01T00:00:00' (Nullable = true)

SELECT TOP 2 `c`.`Id`
FROM `Customers` AS `c`
WHERE `GetCustomerWithMostOrdersAfterDate`(@__startDate_1) = `c`.`Id`");
        }

        public override void Scalar_Function_Where_Parameter_Instance()
        {
            base.Scalar_Function_Where_Parameter_Instance();

            AssertSql(
                @"@__period_1='0'

SELECT TOP 2 `c`.`Id`
FROM `Customers` AS `c`
WHERE `c`.`Id` = `GetCustomerWithMostOrdersAfterDate`(`GetReportingPeriodStartDate`(@__period_1))");
        }

        public override void Scalar_Function_Where_Nested_Instance()
        {
            base.Scalar_Function_Where_Nested_Instance();

            AssertSql(
                @"SELECT TOP 2 `c`.`Id`
FROM `Customers` AS `c`
WHERE `c`.`Id` = `GetCustomerWithMostOrdersAfterDate`(`GetReportingPeriodStartDate`(0))");
        }

        public override void Scalar_Function_Let_Correlated_Instance()
        {
            base.Scalar_Function_Let_Correlated_Instance();

            AssertSql(
                @"SELECT TOP 2 `c`.`LastName`, `CustomerOrderCount`(`c`.`Id`) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = 2");
        }

        public override void Scalar_Function_Let_Not_Correlated_Instance()
        {
            base.Scalar_Function_Let_Not_Correlated_Instance();

            AssertSql(
                @"SELECT TOP 2 `c`.`LastName`, `CustomerOrderCount`(2) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = 2");
        }

        public override void Scalar_Function_Let_Not_Parameter_Instance()
        {
            base.Scalar_Function_Let_Not_Parameter_Instance();

            AssertSql(
                @"@__customerId_1='2'

SELECT TOP 2 `c`.`LastName`, `CustomerOrderCount`(@__customerId_1) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = @__customerId_1");
        }

        public override void Scalar_Function_Let_Nested_Instance()
        {
            base.Scalar_Function_Let_Nested_Instance();

            AssertSql(
                @"@__starCount_1='3'
@__customerId_2='1'

SELECT TOP 2 `c`.`LastName`, `StarValue`(@__starCount_1, `CustomerOrderCount`(@__customerId_2)) AS `OrderCount`
FROM `Customers` AS `c`
WHERE `c`.`Id` = @__customerId_2");
        }

        public override void Scalar_Nested_Function_Unwind_Client_Eval_Select_Instance()
        {
            base.Scalar_Nested_Function_Unwind_Client_Eval_Select_Instance();

            AssertSql(
                @"SELECT `c`.`Id`
FROM `Customers` AS `c`
ORDER BY `c`.`Id`");
        }

        public override void Scalar_Nested_Function_BCL_UDF_Instance()
        {
            base.Scalar_Nested_Function_BCL_UDF_Instance();

            AssertSql(
                @"SELECT TOP 2 `c`.`Id`
FROM `Customers` AS `c`
WHERE 3 = ABS(`CustomerOrderCount`(`c`.`Id`))");
        }

        public override void Scalar_Nested_Function_UDF_BCL_Instance()
        {
            base.Scalar_Nested_Function_UDF_BCL_Instance();

            AssertSql(
                @"SELECT TOP 2 `c`.`Id`
FROM `Customers` AS `c`
WHERE 3 = `CustomerOrderCount`(ABS(`c`.`Id`))");
        }

        #endregion

        #endregion

        public class Jet : UdfFixtureBase
        {
            protected override string StoreName { get; } = "UDFDbFunctionJetTests";
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

            protected override void Seed(DbContext context)
            {
                base.Seed(context);

                context.Database.ExecuteSqlRaw(
                    @"create function `CustomerOrderCount` (@customerId int)
                                                    returns int
                                                    as
                                                    begin
                                                        return (select count(id) from orders where customerId = @customerId);
                                                    end");

                context.Database.ExecuteSqlRaw(
                    @"create function`StarValue` (@starCount int, @value nvarchar(max))
                                                    returns nvarchar(max)
                                                        as
                                                        begin
                                                    return replicate('*', @starCount) + @value
                                                    end");

                context.Database.ExecuteSqlRaw(
                    @"create function`DollarValue` (@starCount int, @value nvarchar(max))
                                                    returns nvarchar(max)
                                                        as
                                                        begin
                                                    return replicate('$', @starCount) + @value
                                                    end");

                context.Database.ExecuteSqlRaw(
                    @"create function `GetReportingPeriodStartDate` (@period int)
                                                    returns DateTime
                                                    as
                                                    begin
                                                        return '1998-01-01'
                                                    end");

                context.Database.ExecuteSqlRaw(
                    @"create function `GetCustomerWithMostOrdersAfterDate` (@searchDate Date)
                                                    returns int
                                                    as
                                                    begin
                                                        return (select top 1 customerId
                                                                from orders
                                                                where orderDate > @searchDate
                                                                group by CustomerId
                                                                order by count(id) desc)
                                                    end");

                context.Database.ExecuteSqlRaw(
                    @"create function `IsTopCustomer` (@customerId int)
                                                    returns bit
                                                    as
                                                    begin
                                                        if(@customerId = 1)
                                                            return 1

                                                        return 0
                                                    end");

                context.Database.ExecuteSqlRaw(
                    @"create function `IdentityString` (@customerName nvarchar(max))
                                                    returns nvarchar(max)
                                                    as
                                                    begin
                                                        return @customerName;
                                                    end");

                context.SaveChanges();
            }
        }

        public void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
