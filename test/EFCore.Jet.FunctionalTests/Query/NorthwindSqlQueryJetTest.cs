// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Data.Common;
using System.Data.OleDb;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

public class NorthwindSqlQueryJetTest : NorthwindSqlQueryTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
{
    public NorthwindSqlQueryJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    public override async Task SqlQueryRaw_over_int(bool async)
    {
        await base.SqlQueryRaw_over_int(async);

        AssertSql(
"""
SELECT `ProductID` FROM `Products`
""");
    }

    public override async Task SqlQuery_composed_Contains(bool async)
    {
        await base.SqlQuery_composed_Contains(async);

        AssertSql(
"""
SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE EXISTS (
    SELECT 1
    FROM (
        SELECT `ProductID` AS `Value` FROM `Products`
    ) AS `t`
    WHERE IIF(`t`.`Value` IS NULL, NULL, CLNG(`t`.`Value`)) = `o`.`OrderID`)
""");
    }

    public override async Task SqlQuery_composed_Join(bool async)
    {
        await base.SqlQuery_composed_Join(async);

        AssertSql(
"""
SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, IIF(`t`.`Value` IS NULL, NULL, CLNG(`t`.`Value`)) AS `p`
FROM `Orders` AS `o`
INNER JOIN (
    SELECT `ProductID` AS `Value` FROM `Products`
) AS `t` ON `o`.`OrderID` = IIF(`t`.`Value` IS NULL, NULL, CLNG(`t`.`Value`))
""");
    }

    public override async Task SqlQuery_over_int_with_parameter(bool async)
    {
        await base.SqlQuery_over_int_with_parameter(async);

        AssertSql(
"""
p0='10'

SELECT "ProductID" FROM "Products" WHERE "ProductID" = @p0
""");
    }

    protected override DbParameter CreateDbParameter(string name, object value)
        => new OleDbParameter { ParameterName = name, Value = value };

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
