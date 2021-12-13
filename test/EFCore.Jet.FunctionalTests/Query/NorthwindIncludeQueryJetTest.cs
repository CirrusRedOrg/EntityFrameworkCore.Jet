// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class NorthwindIncludeQueryJetTest : NorthwindIncludeQueryTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        private bool SupportsOffset => true;

        // ReSharper disable once UnusedParameter.Local
        public NorthwindIncludeQueryJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override async Task Include_list(bool async)
        {
            await base.Include_list(async);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`, `t`.`OrderID`, `t`.`ProductID`, `t`.`Discount`, `t`.`Quantity`, `t`.`UnitPrice`, `t`.`OrderID0`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`
FROM `Products` AS `p`
LEFT JOIN (
    SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`, `o0`.`OrderID` AS `OrderID0`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
    FROM `Order Details` AS `o`
    INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
) AS `t` ON `p`.`ProductID` = `t`.`ProductID`
ORDER BY `p`.`ProductID`, `t`.`OrderID`, `t`.`ProductID`, `t`.`OrderID0`");
        }

        public override async Task Include_reference(bool async)
        {
            await base.Include_reference(async);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`");
        }

        public override async Task Include_when_result_operator(bool async)
        {
            await base.Include_when_result_operator(async);

            AssertSql(
                $@"SELECT CASE
    WHEN EXISTS (
        SELECT 1
        FROM `Customers` AS `c`) THEN True
    ELSE False
END");
        }

        public override async Task Include_collection(bool async)
        {
            await base.Include_collection(async);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
ORDER BY `c`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_collection_with_last(bool async)
        {
            await base.Include_collection_with_last(async);

            AssertSql(
                $@"SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`CompanyName` DESC
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`CompanyName` DESC, `t`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_collection_skip_no_order_by(bool async)
        {
            await base.Include_collection_skip_no_order_by(async);

            if (SupportsOffset)
            {
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY (SELECT 1)
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`CustomerID`, `o`.`OrderID`");
            }
        }

        public override async Task Include_collection_take_no_order_by(bool async)
        {
            await base.Include_collection_take_no_order_by(async);

            if (SupportsOffset)
            {
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`CustomerID`, `o`.`OrderID`");
            }
        }

        public override async Task Include_collection_skip_take_no_order_by(bool async)
        {
            await base.Include_collection_skip_take_no_order_by(async);

            if (SupportsOffset)
            {
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

{AssertSqlHelper.Declaration("@__p_1='5'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY (SELECT 1)
    SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`CustomerID`, `o`.`OrderID`");
            }
        }

        public override async Task Include_reference_and_collection(bool async)
        {
            await base.Include_reference_and_collection(async);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o0`.`OrderID`, `o0`.`ProductID`, `o0`.`Discount`, `o0`.`Quantity`, `o0`.`UnitPrice`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
LEFT JOIN `Order Details` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
ORDER BY `o`.`OrderID`, `o0`.`OrderID`, `o0`.`ProductID`");
        }

        public override async Task Include_references_multi_level(bool async)
        {
            await base.Include_references_multi_level(async);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Order Details` AS `o`
INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
LEFT JOIN `Customers` AS `c` ON `o0`.`CustomerID` = `c`.`CustomerID`");
        }

        public override async Task Include_multiple_references_multi_level(bool async)
        {
            await base.Include_multiple_references_multi_level(async);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Order Details` AS `o`
INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
LEFT JOIN `Customers` AS `c` ON `o0`.`CustomerID` = `c`.`CustomerID`
INNER JOIN `Products` AS `p` ON `o`.`ProductID` = `p`.`ProductID`");
        }

        public override async Task Include_multiple_references_multi_level_reverse(bool async)
        {
            await base.Include_multiple_references_multi_level_reverse(async);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`, `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Order Details` AS `o`
INNER JOIN `Products` AS `p` ON `o`.`ProductID` = `p`.`ProductID`
INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
LEFT JOIN `Customers` AS `c` ON `o0`.`CustomerID` = `c`.`CustomerID`");
        }

        public override async Task Include_references_and_collection_multi_level(bool async)
        {
            await base.Include_references_and_collection_multi_level(async);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o1`.`OrderID`, `o1`.`CustomerID`, `o1`.`EmployeeID`, `o1`.`OrderDate`
FROM `Order Details` AS `o`
INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
LEFT JOIN `Customers` AS `c` ON `o0`.`CustomerID` = `c`.`CustomerID`
LEFT JOIN `Orders` AS `o1` ON `c`.`CustomerID` = `o1`.`CustomerID`
ORDER BY `o`.`OrderID`, `o`.`ProductID`, `o0`.`OrderID`, `o1`.`OrderID`");
        }

        public override async Task Include_multi_level_reference_and_collection_predicate(bool async)
        {
            await base.Include_multi_level_reference_and_collection_predicate(async);

            AssertSql(
                $@"SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`, `t`.`CustomerID0`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
FROM (
    SELECT TOP 2 `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `c`.`CustomerID` AS `CustomerID0`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Orders` AS `o`
    LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
    WHERE `o`.`OrderID` = 10248
) AS `t`
LEFT JOIN `Orders` AS `o0` ON `t`.`CustomerID0` = `o0`.`CustomerID`
ORDER BY `t`.`OrderID`, `o0`.`OrderID`");
        }

        public override async Task Include_multi_level_collection_and_then_include_reference_predicate(bool async)
        {
            await base.Include_multi_level_collection_and_then_include_reference_predicate(async);

            AssertSql(
                $@"SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`, `t0`.`OrderID`, `t0`.`ProductID`, `t0`.`Discount`, `t0`.`Quantity`, `t0`.`UnitPrice`, `t0`.`ProductID0`, `t0`.`Discontinued`, `t0`.`ProductName`, `t0`.`SupplierID`, `t0`.`UnitPrice0`, `t0`.`UnitsInStock`
FROM (
    SELECT TOP 2 `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    WHERE `o`.`OrderID` = 10248
) AS `t`
LEFT JOIN (
    SELECT `o0`.`OrderID`, `o0`.`ProductID`, `o0`.`Discount`, `o0`.`Quantity`, `o0`.`UnitPrice`, `p`.`ProductID` AS `ProductID0`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice` AS `UnitPrice0`, `p`.`UnitsInStock`
    FROM `Order Details` AS `o0`
    INNER JOIN `Products` AS `p` ON `o0`.`ProductID` = `p`.`ProductID`
) AS `t0` ON `t`.`OrderID` = `t0`.`OrderID`
ORDER BY `t`.`OrderID`, `t0`.`OrderID`, `t0`.`ProductID`, `t0`.`ProductID0`");
        }

        public override async Task Include_collection_alias_generation(bool async)
        {
            await base.Include_collection_alias_generation(async);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `o0`.`OrderID`, `o0`.`ProductID`, `o0`.`Discount`, `o0`.`Quantity`, `o0`.`UnitPrice`
FROM `Orders` AS `o`
LEFT JOIN `Order Details` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
ORDER BY `o`.`OrderID`, `o0`.`OrderID`, `o0`.`ProductID`");
        }

        public override async Task Include_collection_order_by_collection_column(bool async)
        {
            await base.Include_collection_order_by_collection_column(async);

            AssertSql(
                $@"SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
FROM (
    SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, (
        SELECT TOP 1 `o`.`OrderDate`
        FROM `Orders` AS `o`
        WHERE `c`.`CustomerID` = `o`.`CustomerID`
        ORDER BY `o`.`OrderDate` DESC) AS `c`
    FROM `Customers` AS `c`
    WHERE `c`.`CustomerID` LIKE 'W' & '%'
    ORDER BY (
        SELECT TOP 1 `o`.`OrderDate`
        FROM `Orders` AS `o`
        WHERE `c`.`CustomerID` = `o`.`CustomerID`
        ORDER BY `o`.`OrderDate` DESC) DESC
) AS `t`
LEFT JOIN `Orders` AS `o0` ON `t`.`CustomerID` = `o0`.`CustomerID`
ORDER BY `t`.`c` DESC, `t`.`CustomerID`, `o0`.`OrderID`");
        }

        public override async Task Include_collection_order_by_key(bool async)
        {
            await base.Include_collection_order_by_key(async);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
ORDER BY `c`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_collection_order_by_non_key(bool async)
        {
            await base.Include_collection_order_by_non_key(async);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
ORDER BY `c`.`City`, `c`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_collection_order_by_non_key_with_take(bool async)
        {
            await base.Include_collection_order_by_non_key_with_take(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`ContactTitle`
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`ContactTitle`, `t`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_collection_order_by_non_key_with_skip(bool async)
        {
            await base.Include_collection_order_by_non_key_with_skip(async);

            if (SupportsOffset)
            {
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`ContactTitle`
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`ContactTitle`, `t`.`CustomerID`, `o`.`OrderID`");
            }
        }

        public override async Task Include_collection_order_by_non_key_with_first_or_default(bool async)
        {
            await base.Include_collection_order_by_non_key_with_first_or_default(async);

            AssertSql(
                $@"SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`CompanyName` DESC
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`CompanyName` DESC, `t`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_collection_order_by_subquery(bool async)
        {
            await base.Include_collection_order_by_subquery(async);

            AssertSql(
                $@"SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
FROM (
    SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, (
        SELECT TOP 1 `o`.`OrderDate`
        FROM `Orders` AS `o`
        WHERE `c`.`CustomerID` = `o`.`CustomerID`
        ORDER BY `o`.`EmployeeID`) AS `c`
    FROM `Customers` AS `c`
    WHERE `c`.`CustomerID` = 'ALFKI'
    ORDER BY (
        SELECT TOP 1 `o`.`OrderDate`
        FROM `Orders` AS `o`
        WHERE `c`.`CustomerID` = `o`.`CustomerID`
        ORDER BY `o`.`EmployeeID`)
) AS `t`
LEFT JOIN `Orders` AS `o0` ON `t`.`CustomerID` = `o0`.`CustomerID`
ORDER BY `t`.`c`, `t`.`CustomerID`, `o0`.`OrderID`");
        }
        
        public override async Task Include_collection_principal_already_tracked(bool async)
        {
            await base.Include_collection_principal_already_tracked(async);

            AssertSql(
                $@"SELECT TOP 2 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'",
                //
                $@"SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT TOP 2 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    WHERE `c`.`CustomerID` = 'ALFKI'
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`CustomerID`, `o`.`OrderID`");
        }
        
        public override async Task Include_collection_with_filter(bool async)
        {
            await base.Include_collection_with_filter(async);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `c`.`CustomerID` = 'ALFKI'
ORDER BY `c`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_collection_with_filter_reordered(bool async)
        {
            await base.Include_collection_with_filter_reordered(async);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `c`.`CustomerID` = 'ALFKI'
ORDER BY `c`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_collection_then_include_collection(bool async)
        {
            await base.Include_collection_then_include_collection(async);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`, `t`.`OrderID0`, `t`.`ProductID`, `t`.`Discount`, `t`.`Quantity`, `t`.`UnitPrice`
FROM `Customers` AS `c`
LEFT JOIN (
    SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `o0`.`OrderID` AS `OrderID0`, `o0`.`ProductID`, `o0`.`Discount`, `o0`.`Quantity`, `o0`.`UnitPrice`
    FROM `Orders` AS `o`
    LEFT JOIN `Order Details` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`
ORDER BY `c`.`CustomerID`, `t`.`OrderID`, `t`.`OrderID0`, `t`.`ProductID`");
        }

        public override async Task Include_collection_then_include_collection_then_include_reference(bool async)
        {
            await base.Include_collection_then_include_collection_then_include_reference(async);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `t0`.`OrderID`, `t0`.`CustomerID`, `t0`.`EmployeeID`, `t0`.`OrderDate`, `t0`.`OrderID0`, `t0`.`ProductID`, `t0`.`Discount`, `t0`.`Quantity`, `t0`.`UnitPrice`, `t0`.`ProductID0`, `t0`.`Discontinued`, `t0`.`ProductName`, `t0`.`SupplierID`, `t0`.`UnitPrice0`, `t0`.`UnitsInStock`
FROM `Customers` AS `c`
LEFT JOIN (
    SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `t`.`OrderID` AS `OrderID0`, `t`.`ProductID`, `t`.`Discount`, `t`.`Quantity`, `t`.`UnitPrice`, `t`.`ProductID0`, `t`.`Discontinued`, `t`.`ProductName`, `t`.`SupplierID`, `t`.`UnitPrice0`, `t`.`UnitsInStock`
    FROM `Orders` AS `o`
    LEFT JOIN (
        SELECT `o0`.`OrderID`, `o0`.`ProductID`, `o0`.`Discount`, `o0`.`Quantity`, `o0`.`UnitPrice`, `p`.`ProductID` AS `ProductID0`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice` AS `UnitPrice0`, `p`.`UnitsInStock`
        FROM `Order Details` AS `o0`
        INNER JOIN `Products` AS `p` ON `o0`.`ProductID` = `p`.`ProductID`
    ) AS `t` ON `o`.`OrderID` = `t`.`OrderID`
) AS `t0` ON `c`.`CustomerID` = `t0`.`CustomerID`
ORDER BY `c`.`CustomerID`, `t0`.`OrderID`, `t0`.`OrderID0`, `t0`.`ProductID`, `t0`.`ProductID0`");
        }

        public override async Task Include_collection_when_projection(bool async)
        {
            await base.Include_collection_when_projection(async);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`");
        }
        
        public override async Task Include_collection_on_additional_from_clause_with_filter(bool async)
        {
            await base.Include_collection_on_additional_from_clause_with_filter(async);

            AssertSql(
                $@"SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `c`.`CustomerID`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`,
(
    SELECT `c0`.`CustomerID`, `c0`.`Address`, `c0`.`City`, `c0`.`CompanyName`, `c0`.`ContactName`, `c0`.`ContactTitle`, `c0`.`Country`, `c0`.`Fax`, `c0`.`Phone`, `c0`.`PostalCode`, `c0`.`Region`
    FROM `Customers` AS `c0`
    WHERE `c0`.`CustomerID` = 'ALFKI'
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `c`.`CustomerID`, `t`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_collection_on_additional_from_clause(bool async)
        {
            await base.Include_collection_on_additional_from_clause(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT `c0`.`CustomerID`, `c0`.`Address`, `c0`.`City`, `c0`.`CompanyName`, `c0`.`ContactName`, `c0`.`ContactTitle`, `c0`.`Country`, `c0`.`Fax`, `c0`.`Phone`, `c0`.`PostalCode`, `c0`.`Region`, `t`.`CustomerID`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`CustomerID`
) AS `t`,
`Customers` AS `c0`
LEFT JOIN `Orders` AS `o` ON `c0`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`CustomerID`, `c0`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_duplicate_collection(bool async)
        {
            await base.Include_duplicate_collection(async);

            if (SupportsOffset)
            {
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `t0`.`CustomerID`, `t0`.`Address`, `t0`.`City`, `t0`.`CompanyName`, `t0`.`ContactName`, `t0`.`ContactTitle`, `t0`.`Country`, `t0`.`Fax`, `t0`.`Phone`, `t0`.`PostalCode`, `t0`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`CustomerID`
) AS `t`,
(
    SELECT `c0`.`CustomerID`, `c0`.`Address`, `c0`.`City`, `c0`.`CompanyName`, `c0`.`ContactName`, `c0`.`ContactTitle`, `c0`.`Country`, `c0`.`Fax`, `c0`.`Phone`, `c0`.`PostalCode`, `c0`.`Region`
    FROM `Customers` AS `c0`
    ORDER BY `c0`.`CustomerID`
    SKIP 2 FETCH NEXT 2 ROWS ONLY
) AS `t0`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
LEFT JOIN `Orders` AS `o0` ON `t0`.`CustomerID` = `o0`.`CustomerID`
ORDER BY `t`.`CustomerID`, `t0`.`CustomerID`, `o`.`OrderID`, `o0`.`OrderID`");
            }
        }

        public override async Task Include_duplicate_collection_result_operator(bool async)
        {
            await base.Include_duplicate_collection_result_operator(async);

            if (SupportsOffset)
            {
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__p_1='1'")}

{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT `t1`.`CustomerID`, `t1`.`Address`, `t1`.`City`, `t1`.`CompanyName`, `t1`.`ContactName`, `t1`.`ContactTitle`, `t1`.`Country`, `t1`.`Fax`, `t1`.`Phone`, `t1`.`PostalCode`, `t1`.`Region`, `t1`.`CustomerID0`, `t1`.`Address0`, `t1`.`City0`, `t1`.`CompanyName0`, `t1`.`ContactName0`, `t1`.`ContactTitle0`, `t1`.`Country0`, `t1`.`Fax0`, `t1`.`Phone0`, `t1`.`PostalCode0`, `t1`.`Region0`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_1")} `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `t0`.`CustomerID` AS `CustomerID0`, `t0`.`Address` AS `Address0`, `t0`.`City` AS `City0`, `t0`.`CompanyName` AS `CompanyName0`, `t0`.`ContactName` AS `ContactName0`, `t0`.`ContactTitle` AS `ContactTitle0`, `t0`.`Country` AS `Country0`, `t0`.`Fax` AS `Fax0`, `t0`.`Phone` AS `Phone0`, `t0`.`PostalCode` AS `PostalCode0`, `t0`.`Region` AS `Region0`
    FROM (
        SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
        FROM `Customers` AS `c`
        ORDER BY `c`.`CustomerID`
    ) AS `t`,
(
        SELECT `c0`.`CustomerID`, `c0`.`Address`, `c0`.`City`, `c0`.`CompanyName`, `c0`.`ContactName`, `c0`.`ContactTitle`, `c0`.`Country`, `c0`.`Fax`, `c0`.`Phone`, `c0`.`PostalCode`, `c0`.`Region`
        FROM `Customers` AS `c0`
        ORDER BY `c0`.`CustomerID`
        SKIP 2 FETCH NEXT 2 ROWS ONLY
    ) AS `t0`
    ORDER BY `t`.`CustomerID`
) AS `t1`
LEFT JOIN `Orders` AS `o` ON `t1`.`CustomerID` = `o`.`CustomerID`
LEFT JOIN `Orders` AS `o0` ON `t1`.`CustomerID0` = `o0`.`CustomerID`
ORDER BY `t1`.`CustomerID`, `t1`.`CustomerID0`, `o`.`OrderID`, `o0`.`OrderID`");
            }
        }

        public override async Task Include_collection_on_join_clause_with_order_by_and_filter(bool async)
        {
            await base.Include_collection_on_join_clause_with_order_by_and_filter(async);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
LEFT JOIN `Orders` AS `o0` ON `c`.`CustomerID` = `o0`.`CustomerID`
WHERE `c`.`CustomerID` = 'ALFKI'
ORDER BY `c`.`City`, `c`.`CustomerID`, `o`.`OrderID`, `o0`.`OrderID`");
        }
        
        public override async Task Include_collection_on_additional_from_clause2(bool async)
        {
            await base.Include_collection_on_additional_from_clause2(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`CustomerID`
) AS `t`,
`Customers` AS `c0`
ORDER BY `t`.`CustomerID`");
        }

        public override async Task Include_where_skip_take_projection(bool async)
        {
            await base.Include_where_skip_take_projection(async);

            if (SupportsOffset)
            {
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__p_0='1'")}

{AssertSqlHelper.Declaration("@__p_1='2'")}

SELECT `o0`.`CustomerID`
FROM (
    SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
    FROM `Order Details` AS `o`
    WHERE `o`.`Quantity` = 10
    ORDER BY `o`.`OrderID`, `o`.`ProductID`
    SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
) AS `t`
INNER JOIN `Orders` AS `o0` ON `t`.`OrderID` = `o0`.`OrderID`
ORDER BY `t`.`OrderID`, `t`.`ProductID`");
            }
        }

        public override async Task Include_duplicate_collection_result_operator2(bool async)
        {
            await base.Include_duplicate_collection_result_operator2(async);

            if (SupportsOffset)
            {
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__p_1='1'")}

{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT `t1`.`CustomerID`, `t1`.`Address`, `t1`.`City`, `t1`.`CompanyName`, `t1`.`ContactName`, `t1`.`ContactTitle`, `t1`.`Country`, `t1`.`Fax`, `t1`.`Phone`, `t1`.`PostalCode`, `t1`.`Region`, `t1`.`CustomerID0`, `t1`.`Address0`, `t1`.`City0`, `t1`.`CompanyName0`, `t1`.`ContactName0`, `t1`.`ContactTitle0`, `t1`.`Country0`, `t1`.`Fax0`, `t1`.`Phone0`, `t1`.`PostalCode0`, `t1`.`Region0`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_1")} `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `t0`.`CustomerID` AS `CustomerID0`, `t0`.`Address` AS `Address0`, `t0`.`City` AS `City0`, `t0`.`CompanyName` AS `CompanyName0`, `t0`.`ContactName` AS `ContactName0`, `t0`.`ContactTitle` AS `ContactTitle0`, `t0`.`Country` AS `Country0`, `t0`.`Fax` AS `Fax0`, `t0`.`Phone` AS `Phone0`, `t0`.`PostalCode` AS `PostalCode0`, `t0`.`Region` AS `Region0`
    FROM (
        SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
        FROM `Customers` AS `c`
        ORDER BY `c`.`CustomerID`
    ) AS `t`,
(
        SELECT `c0`.`CustomerID`, `c0`.`Address`, `c0`.`City`, `c0`.`CompanyName`, `c0`.`ContactName`, `c0`.`ContactTitle`, `c0`.`Country`, `c0`.`Fax`, `c0`.`Phone`, `c0`.`PostalCode`, `c0`.`Region`
        FROM `Customers` AS `c0`
        ORDER BY `c0`.`CustomerID`
        SKIP 2 FETCH NEXT 2 ROWS ONLY
    ) AS `t0`
    ORDER BY `t`.`CustomerID`
) AS `t1`
LEFT JOIN `Orders` AS `o` ON `t1`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t1`.`CustomerID`, `t1`.`CustomerID0`, `o`.`OrderID`");
            }
        }

        public override async Task Include_multiple_references(bool async)
        {
            await base.Include_multiple_references(async);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`, `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Order Details` AS `o`
INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
INNER JOIN `Products` AS `p` ON `o`.`ProductID` = `p`.`ProductID`");
        }

        public override async Task Include_reference_alias_generation(bool async)
        {
            await base.Include_reference_alias_generation(async);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
FROM `Order Details` AS `o`
INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`");
        }

        public override async Task Include_duplicate_reference(bool async)
        {
            await base.Include_duplicate_reference(async);

            if (SupportsOffset)
            {
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `t0`.`OrderID`, `t0`.`CustomerID`, `t0`.`EmployeeID`, `t0`.`OrderDate`, `c0`.`CustomerID`, `c0`.`Address`, `c0`.`City`, `c0`.`CompanyName`, `c0`.`ContactName`, `c0`.`ContactTitle`, `c0`.`Country`, `c0`.`Fax`, `c0`.`Phone`, `c0`.`PostalCode`, `c0`.`Region`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`CustomerID`, `o`.`OrderID`
) AS `t`,
(
    SELECT `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
    FROM `Orders` AS `o0`
    ORDER BY `o0`.`CustomerID`, `o0`.`OrderID`
    SKIP 2 FETCH NEXT 2 ROWS ONLY
) AS `t0`
LEFT JOIN `Customers` AS `c` ON `t`.`CustomerID` = `c`.`CustomerID`
LEFT JOIN `Customers` AS `c0` ON `t0`.`CustomerID` = `c0`.`CustomerID`
ORDER BY `t`.`CustomerID`, `t`.`OrderID`");
            }
        }

        public override async Task Include_duplicate_reference2(bool async)
        {
            await base.Include_duplicate_reference2(async);

            if (SupportsOffset)
            {
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `t0`.`OrderID`, `t0`.`CustomerID`, `t0`.`EmployeeID`, `t0`.`OrderDate`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t`,
(
    SELECT `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
    FROM `Orders` AS `o0`
    ORDER BY `o0`.`OrderID`
    SKIP 2 FETCH NEXT 2 ROWS ONLY
) AS `t0`
LEFT JOIN `Customers` AS `c` ON `t`.`CustomerID` = `c`.`CustomerID`
ORDER BY `t`.`OrderID`");
            }
        }

        public override async Task Include_duplicate_reference3(bool async)
        {
            await base.Include_duplicate_reference3(async);

            if (SupportsOffset)
            {
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`, `t0`.`OrderID`, `t0`.`CustomerID`, `t0`.`EmployeeID`, `t0`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t`,
(
    SELECT `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
    FROM `Orders` AS `o0`
    ORDER BY `o0`.`OrderID`
    SKIP 2 FETCH NEXT 2 ROWS ONLY
) AS `t0`
LEFT JOIN `Customers` AS `c` ON `t0`.`CustomerID` = `c`.`CustomerID`
ORDER BY `t`.`OrderID`");
            }
        }

        public override async Task Include_reference_when_projection(bool async)
        {
            await base.Include_reference_when_projection(async);

            AssertSql(
                $@"SELECT `o`.`CustomerID`
FROM `Orders` AS `o`");
        }

        public override async Task Include_reference_with_filter_reordered(bool async)
        {
            await base.Include_reference_with_filter_reordered(async);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
WHERE `o`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Include_reference_with_filter(bool async)
        {
            await base.Include_reference_with_filter(async);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
WHERE `o`.`CustomerID` = 'ALFKI'");
        }
        
        public override async Task Include_collection_dependent_already_tracked(bool async)
        {
            await base.Include_collection_dependent_already_tracked(async);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = 'ALFKI'",
                //
                $@"SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT TOP 2 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    WHERE `c`.`CustomerID` = 'ALFKI'
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_reference_dependent_already_tracked(bool async)
        {
            await base.Include_reference_dependent_already_tracked(async);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = 'ALFKI'",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`");
        }
        
        public override async Task Include_with_complex_projection(bool async)
        {
            await base.Include_with_complex_projection(async);

            AssertSql(
                $@"SELECT `c`.`CustomerID` AS `Id`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`");
        }

        public override async Task Include_with_complex_projection_does_not_change_ordering_of_projection(bool async)
        {
            await base.Include_with_complex_projection_does_not_change_ordering_of_projection(async);

            AssertSql(
                $@"SELECT `c`.`CustomerID` AS `Id`, (
    SELECT COUNT(*)
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`) AS `TotalOrders`
FROM `Customers` AS `c`
WHERE (`c`.`ContactTitle` = 'Owner') AND ((
    SELECT COUNT(*)
    FROM `Orders` AS `o0`
    WHERE `c`.`CustomerID` = `o0`.`CustomerID`) > 2)
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Include_with_take(bool async)
        {
            await base.Include_with_take(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`City` DESC
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`City` DESC, `t`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_with_skip(bool async)
        {
            await base.Include_with_skip(async);

            if (SupportsOffset)
            {
                AssertSql(
                    $@"{AssertSqlHelper.Declaration("@__p_0='80'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`ContactName`
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`ContactName`, `t`.`CustomerID`, `o`.`OrderID`");
            }
        }

        public override async Task Include_collection_with_multiple_conditional_order_by(bool async)
        {
            await base.Include_collection_with_multiple_conditional_order_by(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`, `o0`.`OrderID`, `o0`.`ProductID`, `o0`.`Discount`, `o0`.`Quantity`, `o0`.`UnitPrice`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, IIF(`o`.`OrderID` > 0, 1, 0) AS `c`, CASE
        WHEN `c`.`CustomerID` IS NOT NULL THEN `c`.`City`
        ELSE ''
    END AS `c0`
    FROM `Orders` AS `o`
    LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
    ORDER BY IIF(`o`.`OrderID` > 0, 1, 0), CASE
        WHEN `c`.`CustomerID` IS NOT NULL THEN `c`.`City`
        ELSE ''
    END
) AS `t`
LEFT JOIN `Order Details` AS `o0` ON `t`.`OrderID` = `o0`.`OrderID`
ORDER BY `t`.`c`, `t`.`c0`, `t`.`OrderID`, `o0`.`OrderID`, `o0`.`ProductID`");
        }

        public override async Task Then_include_collection_order_by_collection_column(bool async)
        {
            await base.Then_include_collection_order_by_collection_column(async);

            AssertSql(
                $@"SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `t0`.`OrderID`, `t0`.`CustomerID`, `t0`.`EmployeeID`, `t0`.`OrderDate`, `t0`.`OrderID0`, `t0`.`ProductID`, `t0`.`Discount`, `t0`.`Quantity`, `t0`.`UnitPrice`
FROM (
    SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, (
        SELECT TOP 1 `o`.`OrderDate`
        FROM `Orders` AS `o`
        WHERE `c`.`CustomerID` = `o`.`CustomerID`
        ORDER BY `o`.`OrderDate` DESC) AS `c`
    FROM `Customers` AS `c`
    WHERE `c`.`CustomerID` LIKE 'W' & '%'
    ORDER BY (
        SELECT TOP 1 `o`.`OrderDate`
        FROM `Orders` AS `o`
        WHERE `c`.`CustomerID` = `o`.`CustomerID`
        ORDER BY `o`.`OrderDate` DESC) DESC
) AS `t`
LEFT JOIN (
    SELECT `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`, `o1`.`OrderID` AS `OrderID0`, `o1`.`ProductID`, `o1`.`Discount`, `o1`.`Quantity`, `o1`.`UnitPrice`
    FROM `Orders` AS `o0`
    LEFT JOIN `Order Details` AS `o1` ON `o0`.`OrderID` = `o1`.`OrderID`
) AS `t0` ON `t`.`CustomerID` = `t0`.`CustomerID`
ORDER BY `t`.`c` DESC, `t`.`CustomerID`, `t0`.`OrderID`, `t0`.`OrderID0`, `t0`.`ProductID`");
        }

        public override async Task Include_collection_with_conditional_order_by(bool async)
        {
            await base.Include_collection_with_conditional_order_by(async);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
ORDER BY CASE
    WHEN `c`.`CustomerID` LIKE 'S' & '%' THEN 1
    ELSE 2
END, `c`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_reference_distinct_is_server_evaluated(bool async)
        {
            await base.Include_reference_distinct_is_server_evaluated(async);

            AssertSql(
                $@"SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM (
    SELECT DISTINCT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    WHERE `o`.`OrderID` < 10250
) AS `t`
LEFT JOIN `Customers` AS `c` ON `t`.`CustomerID` = `c`.`CustomerID`");
        }

        public override async Task Include_collection_distinct_is_server_evaluated(bool async)
        {
            await base.Include_collection_distinct_is_server_evaluated(async);

            AssertSql(
                $@"SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT DISTINCT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    WHERE `c`.`CustomerID` LIKE 'A' & '%'
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_collection_OrderBy_object(bool async)
        {
            await base.Include_collection_OrderBy_object(async);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `o0`.`OrderID`, `o0`.`ProductID`, `o0`.`Discount`, `o0`.`Quantity`, `o0`.`UnitPrice`
FROM `Orders` AS `o`
LEFT JOIN `Order Details` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
WHERE `o`.`OrderID` < 10250
ORDER BY `o`.`OrderID`, `o0`.`OrderID`, `o0`.`ProductID`");
        }

        public override async Task Include_collection_OrderBy_empty_list_contains(bool async)
        {
            await base.Include_collection_OrderBy_empty_list_contains(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_1='1'")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` LIKE 'A' & '%'
ORDER BY (SELECT 1), `c`.`CustomerID`
SKIP {AssertSqlHelper.Parameter("@__p_1")}",
                //
                $@"{AssertSqlHelper.Declaration("@__p_1='1'")}

SELECT [c.Orders].`OrderID`, [c.Orders].`CustomerID`, [c.Orders].`EmployeeID`, [c.Orders].`OrderDate`
FROM `Orders` AS [c.Orders]
INNER JOIN (
    SELECT `c0`.`CustomerID`, False AS `c`
    FROM `Customers` AS `c0`
    WHERE `c0`.`CustomerID` LIKE 'A' & '%'
    ORDER BY `c`, `c0`.`CustomerID`
    SKIP {AssertSqlHelper.Parameter("@__p_1")}
) AS `t` ON [c.Orders].`CustomerID` = `t`.`CustomerID`
ORDER BY `t`.`c`, `t`.`CustomerID`");
        }

        public override async Task Include_collection_OrderBy_empty_list_does_not_contains(bool async)
        {
            await base.Include_collection_OrderBy_empty_list_does_not_contains(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_1='1'")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` LIKE 'A' & '%'
ORDER BY (SELECT 1), `c`.`CustomerID`
SKIP {AssertSqlHelper.Parameter("@__p_1")}",
                //
                $@"{AssertSqlHelper.Declaration("@__p_1='1'")}

SELECT [c.Orders].`OrderID`, [c.Orders].`CustomerID`, [c.Orders].`EmployeeID`, [c.Orders].`OrderDate`
FROM `Orders` AS [c.Orders]
INNER JOIN (
    SELECT `c0`.`CustomerID`, True AS `c`
    FROM `Customers` AS `c0`
    WHERE `c0`.`CustomerID` LIKE 'A' & '%'
    ORDER BY `c`, `c0`.`CustomerID`
    SKIP {AssertSqlHelper.Parameter("@__p_1")}
) AS `t` ON [c.Orders].`CustomerID` = `t`.`CustomerID`
ORDER BY `t`.`c`, `t`.`CustomerID`");
        }

        public override async Task Include_collection_OrderBy_list_contains(bool async)
        {
            await base.Include_collection_OrderBy_list_contains(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_1='1'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, IIF(`c`.`CustomerID` IN ('ALFKI'), 1, 0) AS `c`
    FROM `Customers` AS `c`
    WHERE `c`.`CustomerID` LIKE 'A' & '%'
    ORDER BY IIF(`c`.`CustomerID` IN ('ALFKI'), 1, 0)
    SKIP {AssertSqlHelper.Parameter("@__p_1")}
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`c`, `t`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_collection_OrderBy_list_does_not_contains(bool async)
        {
            await base.Include_collection_OrderBy_list_does_not_contains(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_1='1'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, IIF(`c`.`CustomerID` NOT IN ('ALFKI'), 1, 0) AS `c`
    FROM `Customers` AS `c`
    WHERE `c`.`CustomerID` LIKE 'A' & '%'
    ORDER BY IIF(`c`.`CustomerID` NOT IN ('ALFKI'), 1, 0)
    SKIP {AssertSqlHelper.Parameter("@__p_1")}
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`c`, `t`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Include_is_not_ignored_when_projection_contains_client_method_and_complex_expression(
            bool async)
        {
            await base.Include_is_not_ignored_when_projection_contains_client_method_and_complex_expression(async);

            AssertSql(
                $@"SELECT IIF(`e0`.`EmployeeID` IS NOT NULL, 1, 0), `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`, `e0`.`EmployeeID`, `e0`.`City`, `e0`.`Country`, `e0`.`FirstName`, `e0`.`ReportsTo`, `e0`.`Title`
FROM `Employees` AS `e`
LEFT JOIN `Employees` AS `e0` ON `e`.`ReportsTo` = `e0`.`EmployeeID`
WHERE (`e`.`EmployeeID` = 1) OR (`e`.`EmployeeID` = 2)");
        }
        
        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        protected override void ClearLog()
            => Fixture.TestSqlLoggerFactory.Clear();
    }
}
