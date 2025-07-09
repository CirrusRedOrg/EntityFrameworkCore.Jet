// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Data.Common;
using System.Data.Odbc;
using System.Data.OleDb;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;
#nullable disable
namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

public class SqlQueryJetTest : SqlQueryTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
{
    public SqlQueryJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task SqlQueryRaw_queryable_simple(bool async)
    {
        await base.SqlQueryRaw_queryable_simple(async);

        AssertSql(
"""
SELECT * FROM `Customers` WHERE `ContactName` LIKE '%z%'
""");
    }

    public override async Task SqlQueryRaw_queryable_simple_columns_out_of_order(bool async)
    {
        await base.SqlQueryRaw_queryable_simple_columns_out_of_order(async);

        AssertSql(
"""
SELECT `Region`, `PostalCode`, `Phone`, `Fax`, `CustomerID`, `Country`, `ContactTitle`, `ContactName`, `CompanyName`, `City`, `Address` FROM `Customers`
""");
    }

    public override async Task SqlQueryRaw_queryable_simple_columns_out_of_order_and_extra_columns(bool async)
    {
        await base.SqlQueryRaw_queryable_simple_columns_out_of_order_and_extra_columns(async);

        AssertSql(
"""
SELECT `Region`, `PostalCode`, `PostalCode` AS `Foo`, `Phone`, `Fax`, `CustomerID`, `Country`, `ContactTitle`, `ContactName`, `CompanyName`, `City`, `Address` FROM `Customers`
""");
    }

    public override async Task SqlQueryRaw_queryable_composed(bool async)
    {
        await base.SqlQueryRaw_queryable_composed(async);

        AssertSql(
"""
SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`
FROM (
    SELECT * FROM `Customers`
) AS `m`
WHERE `m`.`ContactName` LIKE '%z%'
""");
    }

    public override async Task SqlQueryRaw_queryable_composed_after_removing_whitespaces(bool async)
    {
        await base.SqlQueryRaw_queryable_composed_after_removing_whitespaces(async);

        AssertSql(
"""
SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`
FROM (

        


    SELECT
    * FROM `Customers`
) AS `m`
WHERE `m`.`ContactName` LIKE '%z%'
""");
    }

    public override async Task SqlQueryRaw_queryable_composed_compiled(bool async)
    {
        await base.SqlQueryRaw_queryable_composed_compiled(async);

        AssertSql(
"""
SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`
FROM (
    SELECT * FROM `Customers`
) AS `m`
WHERE `m`.`ContactName` LIKE '%z%'
""");
    }

    public override async Task SqlQueryRaw_queryable_composed_compiled_with_DbParameter(bool async)
    {
        await base.SqlQueryRaw_queryable_composed_compiled_with_DbParameter(async);

        AssertSql(
$"""
customer='CONSH' (Nullable = false) (Size = 5)

SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`
FROM (
    SELECT * FROM `Customers` WHERE `CustomerID` = {AssertSqlHelper.Parameter("@customer")}
) AS `m`
WHERE `m`.`ContactName` LIKE '%z%'
""");
    }

    public override async Task SqlQueryRaw_queryable_composed_compiled_with_nameless_DbParameter(bool async)
    {
        await base.SqlQueryRaw_queryable_composed_compiled_with_nameless_DbParameter(async);

        AssertSql(
$"""
p0='CONSH' (Nullable = false) (Size = 5)

SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`
FROM (
    SELECT * FROM `Customers` WHERE `CustomerID` = {AssertSqlHelper.Parameter("@p0")}
) AS `m`
WHERE `m`.`ContactName` LIKE '%z%'
""");
    }

    public override async Task SqlQueryRaw_queryable_composed_compiled_with_parameter(bool async)
    {
        await base.SqlQueryRaw_queryable_composed_compiled_with_parameter(async);

        AssertSql(
"""
SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`
FROM (
    SELECT * FROM `Customers` WHERE `CustomerID` = 'CONSH'
) AS `m`
WHERE `m`.`ContactName` LIKE '%z%'
""");
    }

    public override async Task SqlQueryRaw_composed_contains(bool async)
    {
        await base.SqlQueryRaw_composed_contains(async);

        AssertSql(
"""
SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`
FROM (
    SELECT * FROM `Customers`
) AS `m`
WHERE `m`.`CustomerID` IN (
    SELECT `m0`.`CustomerID`
    FROM (
        SELECT * FROM `Orders`
    ) AS `m0`
)
""");
    }

    public override async Task SqlQueryRaw_queryable_multiple_composed(bool async)
    {
        await base.SqlQueryRaw_queryable_multiple_composed(async);

        AssertSql(
"""
SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`, `m0`.`CustomerID`, `m0`.`EmployeeID`, `m0`.`Freight`, `m0`.`OrderDate`, `m0`.`OrderID`, `m0`.`RequiredDate`, `m0`.`ShipAddress`, `m0`.`ShipCity`, `m0`.`ShipCountry`, `m0`.`ShipName`, `m0`.`ShipPostalCode`, `m0`.`ShipRegion`, `m0`.`ShipVia`, `m0`.`ShippedDate`
FROM (
    SELECT * FROM `Customers`
) AS `m`,
(
    SELECT * FROM `Orders`
) AS `m0`
WHERE `m`.`CustomerID` = `m0`.`CustomerID`
""");
    }

    public override async Task SqlQueryRaw_queryable_multiple_composed_with_closure_parameters(bool async)
    {
        await base.SqlQueryRaw_queryable_multiple_composed_with_closure_parameters(async);

        AssertSql(
$"""
p0='1997-01-01T00:00:00.0000000' (DbType = DateTime)
p1='1998-01-01T00:00:00.0000000' (DbType = DateTime)

SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`, `m0`.`CustomerID`, `m0`.`EmployeeID`, `m0`.`Freight`, `m0`.`OrderDate`, `m0`.`OrderID`, `m0`.`RequiredDate`, `m0`.`ShipAddress`, `m0`.`ShipCity`, `m0`.`ShipCountry`, `m0`.`ShipName`, `m0`.`ShipPostalCode`, `m0`.`ShipRegion`, `m0`.`ShipVia`, `m0`.`ShippedDate`
FROM (
    SELECT * FROM `Customers`
) AS `m`,
(
    SELECT * FROM `Orders` WHERE `OrderDate` BETWEEN {AssertSqlHelper.Parameter("@p0")} AND {AssertSqlHelper.Parameter("@p1")}
) AS `m0`
WHERE `m`.`CustomerID` = `m0`.`CustomerID`
""");
    }

    public override async Task SqlQueryRaw_queryable_multiple_composed_with_parameters_and_closure_parameters(bool async)
    {
        await base.SqlQueryRaw_queryable_multiple_composed_with_parameters_and_closure_parameters(async);

        AssertSql(
$"""
p0='London' (Size = 255)
p1='1997-01-01T00:00:00.0000000' (DbType = DateTime)
p2='1998-01-01T00:00:00.0000000' (DbType = DateTime)

SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`, `m0`.`CustomerID`, `m0`.`EmployeeID`, `m0`.`Freight`, `m0`.`OrderDate`, `m0`.`OrderID`, `m0`.`RequiredDate`, `m0`.`ShipAddress`, `m0`.`ShipCity`, `m0`.`ShipCountry`, `m0`.`ShipName`, `m0`.`ShipPostalCode`, `m0`.`ShipRegion`, `m0`.`ShipVia`, `m0`.`ShippedDate`
FROM (
    SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")}
) AS `m`,
(
    SELECT * FROM `Orders` WHERE `OrderDate` BETWEEN {AssertSqlHelper.Parameter("@p1")} AND {AssertSqlHelper.Parameter("@p2")}
) AS `m0`
WHERE `m`.`CustomerID` = `m0`.`CustomerID`
""",
//
$"""
p0='Berlin' (Size = 255)
p1='1998-04-01T00:00:00.0000000' (DbType = DateTime)
p2='1998-05-01T00:00:00.0000000' (DbType = DateTime)

SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`, `m0`.`CustomerID`, `m0`.`EmployeeID`, `m0`.`Freight`, `m0`.`OrderDate`, `m0`.`OrderID`, `m0`.`RequiredDate`, `m0`.`ShipAddress`, `m0`.`ShipCity`, `m0`.`ShipCountry`, `m0`.`ShipName`, `m0`.`ShipPostalCode`, `m0`.`ShipRegion`, `m0`.`ShipVia`, `m0`.`ShippedDate`
FROM (
    SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")}
) AS `m`,
(
    SELECT * FROM `Orders` WHERE `OrderDate` BETWEEN {AssertSqlHelper.Parameter("@p1")} AND {AssertSqlHelper.Parameter("@p2")}
) AS `m0`
WHERE `m`.`CustomerID` = `m0`.`CustomerID`
""");
    }

    public override async Task SqlQueryRaw_queryable_multiple_line_query(bool async)
    {
        await base.SqlQueryRaw_queryable_multiple_line_query(async);

        AssertSql(
"""
SELECT *
FROM `Customers`
WHERE `City` = 'London'
""");
    }

    public override async Task SqlQueryRaw_queryable_composed_multiple_line_query(bool async)
    {
        await base.SqlQueryRaw_queryable_composed_multiple_line_query(async);

        AssertSql(
"""
SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`
FROM (
    SELECT *
    FROM `Customers`
) AS `m`
WHERE `m`.`City` = 'London'
""");
    }

    public override async Task SqlQueryRaw_queryable_with_parameters(bool async)
    {
        await base.SqlQueryRaw_queryable_with_parameters(async);

        AssertSql(
$"""
p0='London' (Size = 255)
p1='Sales Representative' (Size = 255)

SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")} AND `ContactTitle` = {AssertSqlHelper.Parameter("@p1")}
""");
    }

    public override async Task SqlQueryRaw_queryable_with_parameters_inline(bool async)
    {
        await base.SqlQueryRaw_queryable_with_parameters_inline(async);

        AssertSql(
$"""
p0='London' (Size = 255)
p1='Sales Representative' (Size = 255)

SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")} AND `ContactTitle` = {AssertSqlHelper.Parameter("@p1")}
""");
    }

    public override async Task SqlQuery_queryable_with_parameters_interpolated(bool async)
    {
        await base.SqlQuery_queryable_with_parameters_interpolated(async);

        AssertSql(
$"""
p0='London' (Size = 255)
p1='Sales Representative' (Size = 255)

SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")} AND `ContactTitle` = {AssertSqlHelper.Parameter("@p1")}
""");
    }

    public override async Task SqlQuery_queryable_with_parameters_inline_interpolated(bool async)
    {
        await base.SqlQuery_queryable_with_parameters_inline_interpolated(async);

        AssertSql(
$"""
p0='London' (Size = 255)
p1='Sales Representative' (Size = 255)

SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")} AND `ContactTitle` = {AssertSqlHelper.Parameter("@p1")}
""");
    }

    public override async Task SqlQuery_queryable_multiple_composed_with_parameters_and_closure_parameters_interpolated(
        bool async)
    {
        await base.SqlQuery_queryable_multiple_composed_with_parameters_and_closure_parameters_interpolated(async);

        AssertSql(
$"""
p0='London' (Size = 255)
p1='1997-01-01T00:00:00.0000000' (DbType = DateTime)
p2='1998-01-01T00:00:00.0000000' (DbType = DateTime)

SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`, `m0`.`CustomerID`, `m0`.`EmployeeID`, `m0`.`Freight`, `m0`.`OrderDate`, `m0`.`OrderID`, `m0`.`RequiredDate`, `m0`.`ShipAddress`, `m0`.`ShipCity`, `m0`.`ShipCountry`, `m0`.`ShipName`, `m0`.`ShipPostalCode`, `m0`.`ShipRegion`, `m0`.`ShipVia`, `m0`.`ShippedDate`
FROM (
    SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")}
) AS `m`,
(
    SELECT * FROM `Orders` WHERE `OrderDate` BETWEEN {AssertSqlHelper.Parameter("@p1")} AND {AssertSqlHelper.Parameter("@p2")}
) AS `m0`
WHERE `m`.`CustomerID` = `m0`.`CustomerID`
""",
//
$"""
p0='Berlin' (Size = 255)
p1='1998-04-01T00:00:00.0000000' (DbType = DateTime)
p2='1998-05-01T00:00:00.0000000' (DbType = DateTime)

SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`, `m0`.`CustomerID`, `m0`.`EmployeeID`, `m0`.`Freight`, `m0`.`OrderDate`, `m0`.`OrderID`, `m0`.`RequiredDate`, `m0`.`ShipAddress`, `m0`.`ShipCity`, `m0`.`ShipCountry`, `m0`.`ShipName`, `m0`.`ShipPostalCode`, `m0`.`ShipRegion`, `m0`.`ShipVia`, `m0`.`ShippedDate`
FROM (
    SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")}
) AS `m`,
(
    SELECT * FROM `Orders` WHERE `OrderDate` BETWEEN {AssertSqlHelper.Parameter("@p1")} AND {AssertSqlHelper.Parameter("@p2")}
) AS `m0`
WHERE `m`.`CustomerID` = `m0`.`CustomerID`
""");
    }

    public override async Task SqlQueryRaw_queryable_with_null_parameter(bool async)
    {
        await base.SqlQueryRaw_queryable_with_null_parameter(async);

        AssertSql(
$"""
p0=NULL (Nullable = false)
p0=NULL (Nullable = false)

SELECT * FROM `Employees` WHERE `ReportsTo` = {AssertSqlHelper.Parameter("@p0")} OR (`ReportsTo` IS NULL AND {AssertSqlHelper.Parameter("@p0")} IS NULL)
""");
    }

    public override async Task<string> SqlQueryRaw_queryable_with_parameters_and_closure(bool async)
    {
        var queryString = await base.SqlQueryRaw_queryable_with_parameters_and_closure(async);

        AssertSql(
            """
p0='London' (Size = 255)
@contactTitle='Sales Representative' (Size = 30)

SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`
FROM (
    SELECT * FROM `Customers` WHERE `City` = @p0
) AS `m`
WHERE `m`.`ContactTitle` = @contactTitle
""");

        return null;
    }

    public override async Task SqlQueryRaw_queryable_simple_cache_key_includes_query_string(bool async)
    {
        await base.SqlQueryRaw_queryable_simple_cache_key_includes_query_string(async);

        AssertSql(
"""
SELECT * FROM `Customers` WHERE `City` = 'London'
""",
//
"""
SELECT * FROM `Customers` WHERE `City` = 'Seattle'
""");
    }

    public override async Task SqlQueryRaw_queryable_with_parameters_cache_key_includes_parameters(bool async)
    {
        await base.SqlQueryRaw_queryable_with_parameters_cache_key_includes_parameters(async);

        AssertSql(
$"""
p0='London' (Size = 255)
p1='Sales Representative' (Size = 255)

SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")} AND `ContactTitle` = {AssertSqlHelper.Parameter("@p1")}
""",
//
$"""
p0='Madrid' (Size = 255)
p1='Accounting Manager' (Size = 255)

SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")} AND `ContactTitle` = {AssertSqlHelper.Parameter("@p1")}
""");
    }

    public override async Task SqlQueryRaw_queryable_simple_as_no_tracking_not_composed(bool async)
    {
        await base.SqlQueryRaw_queryable_simple_as_no_tracking_not_composed(async);

        AssertSql(
"""
SELECT * FROM `Customers`
""");
    }

    public override async Task SqlQueryRaw_queryable_simple_projection_composed(bool async)
    {
        await base.SqlQueryRaw_queryable_simple_projection_composed(async);

        AssertSql(
"""
SELECT `m`.`ProductName`
FROM (
    SELECT *
    FROM `Products`
    WHERE `Discontinued` <> TRUE
    AND ((`UnitsInStock` + `UnitsOnOrder`) < `ReorderLevel`)
) AS `m`
""");
    }

    public override async Task SqlQueryRaw_annotations_do_not_affect_successive_calls(bool async)
    {
        await base.SqlQueryRaw_annotations_do_not_affect_successive_calls(async);

        AssertSql(
"""
SELECT * FROM `Customers` WHERE `ContactName` LIKE '%z%'
""",
//
"""
SELECT * FROM `Customers`
""");
    }

    public override async Task SqlQueryRaw_composed_with_predicate(bool async)
    {
        await base.SqlQueryRaw_composed_with_predicate(async);

        AssertSql(
            """
SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`
FROM (
    SELECT * FROM `Customers`
) AS `m`
WHERE MID(`m`.`ContactName`, 0 + 1, 1) = MID(`m`.`CompanyName`, 0 + 1, 1)
""");
    }

    public override async Task SqlQueryRaw_with_dbParameter(bool async)
    {
        await base.SqlQueryRaw_with_dbParameter(async);

        AssertSql(
$"""
@city='London' (Nullable = false) (Size = 6)

SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@city")}
""");
    }

    public override async Task SqlQueryRaw_with_dbParameter_without_name_prefix(bool async)
    {
        await base.SqlQueryRaw_with_dbParameter_without_name_prefix(async);
        AssertSql(
$"""
city='London' (Nullable = false) (Size = 6)

SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@city")}
""");
    }

    public override async Task SqlQueryRaw_with_dbParameter_mixed(bool async)
    {
        await base.SqlQueryRaw_with_dbParameter_mixed(async);

        AssertSql(
            """
p0='London' (Size = 255)
@title='Sales Representative' (Nullable = false) (Size = 20)

SELECT * FROM `Customers` WHERE `City` = @p0 AND `ContactTitle` = @title
""",
            //
            """
@city='London' (Nullable = false) (Size = 6)
p0='Sales Representative' (Size = 255)

SELECT * FROM `Customers` WHERE `City` = @city AND `ContactTitle` = @p0
""");
    }

    public override async Task SqlQueryRaw_with_db_parameters_called_multiple_times(bool async)
    {
        await base.SqlQueryRaw_with_db_parameters_called_multiple_times(async);

        AssertSql(
$"""
@id='ALFKI' (Nullable = false) (Size = 5)

SELECT * FROM `Customers` WHERE `CustomerID` = {AssertSqlHelper.Parameter("@id")}
""",
//
$"""
@id='ALFKI' (Nullable = false) (Size = 5)

SELECT * FROM `Customers` WHERE `CustomerID` = {AssertSqlHelper.Parameter("@id")}
""");
    }

    public override async Task SqlQuery_with_inlined_db_parameter(bool async)
    {
        await base.SqlQuery_with_inlined_db_parameter(async);

        AssertSql(
$"""
@somename='ALFKI' (Nullable = false) (Size = 5)

SELECT * FROM `Customers` WHERE `CustomerID` = {AssertSqlHelper.Parameter("@somename")}
""");
    }

    public override async Task SqlQuery_with_inlined_db_parameter_without_name_prefix(bool async)
    {
        await base.SqlQuery_with_inlined_db_parameter_without_name_prefix(async);

        AssertSql(
$"""
somename='ALFKI' (Nullable = false) (Size = 5)

SELECT * FROM `Customers` WHERE `CustomerID` = {AssertSqlHelper.Parameter("@somename")}
""");
    }

    public override async Task SqlQuery_parameterization_issue_12213(bool async)
    {
        await base.SqlQuery_parameterization_issue_12213(async);

        AssertSql(
            """
p0='10300'

SELECT `m`.`OrderID`
FROM (
    SELECT * FROM `Orders` WHERE `OrderID` >= @p0
) AS `m`
""",
            //
            """
@max='10400'
p0='10300'

SELECT `m`.`OrderID`
FROM (
    SELECT * FROM `Orders`
) AS `m`
WHERE `m`.`OrderID` <= @max AND `m`.`OrderID` IN (
    SELECT `m0`.`OrderID`
    FROM (
        SELECT * FROM `Orders` WHERE `OrderID` >= @p0
    ) AS `m0`
)
""",
            //
            """
@max='10400'
p0='10300'

SELECT `m`.`OrderID`
FROM (
    SELECT * FROM `Orders`
) AS `m`
WHERE `m`.`OrderID` <= @max AND `m`.`OrderID` IN (
    SELECT `m0`.`OrderID`
    FROM (
        SELECT * FROM `Orders` WHERE `OrderID` >= @p0
    ) AS `m0`
)
""");
    }

    public override async Task SqlQueryRaw_does_not_parameterize_interpolated_string(bool async)
    {
        await base.SqlQueryRaw_does_not_parameterize_interpolated_string(async);

        AssertSql(
$"""
p0='10250'

SELECT * FROM `Orders` WHERE `OrderID` < {AssertSqlHelper.Parameter("@p0")}
""");
    }

    public override async Task SqlQueryRaw_with_set_operation(bool async)
    {
        await base.SqlQueryRaw_with_set_operation(async);

        AssertSql(
"""
SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`
FROM (
    SELECT * FROM `Customers` WHERE `City` = 'London'
) AS `m`
UNION ALL
SELECT `m0`.`Address`, `m0`.`City`, `m0`.`CompanyName`, `m0`.`ContactName`, `m0`.`ContactTitle`, `m0`.`Country`, `m0`.`CustomerID`, `m0`.`Fax`, `m0`.`Phone`, `m0`.`Region`, `m0`.`PostalCode`
FROM (
    SELECT * FROM `Customers` WHERE `City` = 'Berlin'
) AS `m0`
""");
    }

    public override async Task Line_endings_after_Select(bool async)
    {
        await base.Line_endings_after_Select(async);

        AssertSql(
"""
SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`, `m`.`Country`, `m`.`CustomerID`, `m`.`Fax`, `m`.`Phone`, `m`.`Region`, `m`.`PostalCode`
FROM (
    SELECT
    * FROM `Customers`
) AS `m`
WHERE `m`.`City` = 'Seattle'
""");
    }

    public override async Task SqlQueryRaw_in_subquery_with_dbParameter(bool async)
    {
        await base.SqlQueryRaw_in_subquery_with_dbParameter(async);

        AssertSql(
$"""
@city='London' (Nullable = false) (Size = 6)

SELECT `m`.`CustomerID`, `m`.`EmployeeID`, `m`.`Freight`, `m`.`OrderDate`, `m`.`OrderID`, `m`.`RequiredDate`, `m`.`ShipAddress`, `m`.`ShipCity`, `m`.`ShipCountry`, `m`.`ShipName`, `m`.`ShipPostalCode`, `m`.`ShipRegion`, `m`.`ShipVia`, `m`.`ShippedDate`
FROM (
    SELECT * FROM `Orders`
) AS `m`
WHERE `m`.`CustomerID` IN (
    SELECT `m0`.`CustomerID`
    FROM (
        SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@city")}
    ) AS `m0`
)
""");
    }

    public override async Task SqlQueryRaw_in_subquery_with_positional_dbParameter_without_name(bool async)
    {
        await base.SqlQueryRaw_in_subquery_with_positional_dbParameter_without_name(async);

        AssertSql(
$"""
p0='London' (Nullable = false) (Size = 6)

SELECT `m`.`CustomerID`, `m`.`EmployeeID`, `m`.`Freight`, `m`.`OrderDate`, `m`.`OrderID`, `m`.`RequiredDate`, `m`.`ShipAddress`, `m`.`ShipCity`, `m`.`ShipCountry`, `m`.`ShipName`, `m`.`ShipPostalCode`, `m`.`ShipRegion`, `m`.`ShipVia`, `m`.`ShippedDate`
FROM (
    SELECT * FROM `Orders`
) AS `m`
WHERE `m`.`CustomerID` IN (
    SELECT `m0`.`CustomerID`
    FROM (
        SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")}
    ) AS `m0`
)
""");
    }

    public override async Task SqlQueryRaw_in_subquery_with_positional_dbParameter_with_name(bool async)
    {
        await base.SqlQueryRaw_in_subquery_with_positional_dbParameter_with_name(async);

        AssertSql(
$"""
@city='London' (Nullable = false) (Size = 6)

SELECT `m`.`CustomerID`, `m`.`EmployeeID`, `m`.`Freight`, `m`.`OrderDate`, `m`.`OrderID`, `m`.`RequiredDate`, `m`.`ShipAddress`, `m`.`ShipCity`, `m`.`ShipCountry`, `m`.`ShipName`, `m`.`ShipPostalCode`, `m`.`ShipRegion`, `m`.`ShipVia`, `m`.`ShippedDate`
FROM (
    SELECT * FROM `Orders`
) AS `m`
WHERE `m`.`CustomerID` IN (
    SELECT `m0`.`CustomerID`
    FROM (
        SELECT * FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@city")}
    ) AS `m0`
)
""");
    }

    public override async Task SqlQueryRaw_with_dbParameter_mixed_in_subquery(bool async)
    {
        await base.SqlQueryRaw_with_dbParameter_mixed_in_subquery(async);

        AssertSql(
            """
p0='London' (Size = 255)
@title='Sales Representative' (Nullable = false) (Size = 20)

SELECT `m`.`CustomerID`, `m`.`EmployeeID`, `m`.`Freight`, `m`.`OrderDate`, `m`.`OrderID`, `m`.`RequiredDate`, `m`.`ShipAddress`, `m`.`ShipCity`, `m`.`ShipCountry`, `m`.`ShipName`, `m`.`ShipPostalCode`, `m`.`ShipRegion`, `m`.`ShipVia`, `m`.`ShippedDate`
FROM (
    SELECT * FROM `Orders`
) AS `m`
WHERE `m`.`CustomerID` IN (
    SELECT `m0`.`CustomerID`
    FROM (
        SELECT * FROM `Customers` WHERE `City` = @p0 AND `ContactTitle` = @title
    ) AS `m0`
)
""",
            //
            """
@city='London' (Nullable = false) (Size = 6)
p0='Sales Representative' (Size = 255)

SELECT `m`.`CustomerID`, `m`.`EmployeeID`, `m`.`Freight`, `m`.`OrderDate`, `m`.`OrderID`, `m`.`RequiredDate`, `m`.`ShipAddress`, `m`.`ShipCity`, `m`.`ShipCountry`, `m`.`ShipName`, `m`.`ShipPostalCode`, `m`.`ShipRegion`, `m`.`ShipVia`, `m`.`ShippedDate`
FROM (
    SELECT * FROM `Orders`
) AS `m`
WHERE `m`.`CustomerID` IN (
    SELECT `m0`.`CustomerID`
    FROM (
        SELECT * FROM `Customers` WHERE `City` = @city AND `ContactTitle` = @p0
    ) AS `m0`
)
""");
    }

    public override async Task SqlQueryRaw_composed_with_common_table_expression(bool async)
    {
        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() => base.SqlQueryRaw_composed_with_common_table_expression(async));

        Assert.Equal(RelationalStrings.FromSqlNonComposable, exception.Message);
    }

    protected override DbParameter CreateDbParameter(string name, object value)
    {
        if (((JetTestStore)Fixture.TestStore).IsOleDb())
        {
            return new OleDbParameter { ParameterName = name, Value = value };
        }
        return new OdbcParameter { ParameterName = name, Value = value };
    }

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
