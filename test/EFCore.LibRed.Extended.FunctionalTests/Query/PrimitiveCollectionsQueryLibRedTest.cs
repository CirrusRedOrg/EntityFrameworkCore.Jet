// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.Jet.Internal;
using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using EntityFrameworkCore.LibRed.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.Query;

/// <summary>
///     Runs all primitive collection tests with SQL Server compatibility level 120 (SQL Server 2014), which doesn't support OPENJSON.
///     This exercises the older translation paths for e.g. Contains, to make sure things work for providers with no queryable constant/
///     parameter support.
/// </summary>
public class PrimitiveCollectionsQueryLibRedTest : PrimitiveCollectionsQueryRelationalTestBase<
    PrimitiveCollectionsQueryLibRedTest.PrimitiveCollectionsQueryLibRedFixture>
{
    public override int? NumberOfValuesForHugeParameterCollectionTests { get; } = 5000;

    public PrimitiveCollectionsQueryLibRedTest(PrimitiveCollectionsQueryLibRedFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task Inline_collection_of_ints_Contains()
    {
        await base.Inline_collection_of_ints_Contains();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (10, 999)
""");
    }

    public override async Task Inline_collection_of_nullable_ints_Contains()
    {
        await base.Inline_collection_of_nullable_ints_Contains();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableInt` IN (10, 999)
""");
    }

    public override async Task Inline_collection_of_nullable_ints_Contains_null()
    {
        await base.Inline_collection_of_nullable_ints_Contains_null();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableInt` IS NULL OR `p`.`NullableInt` = 999
""");
    }

    public override async Task Inline_collection_Count_with_zero_values()
    {
        await base.Inline_collection_Count_with_zero_values();

        AssertSql();
    }

    public override async Task Inline_collection_Count_with_one_value()
    {
        await base.Inline_collection_Count_with_one_value();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT CLNG(2) AS `Value`) AS `v`
    WHERE `v`.`Value` > `p`.`Id`) = 1
""");
    }

    public override async Task Inline_collection_Count_with_two_values()
    {
        await base.Inline_collection_Count_with_two_values();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT CLNG(2) AS `Value` UNION ALL VALUES (999)) AS `v`
    WHERE `v`.`Value` > `p`.`Id`) = 1
""");
    }

    public override async Task Inline_collection_Count_with_three_values()
    {
        await base.Inline_collection_Count_with_three_values();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT CLNG(2) AS `Value` UNION ALL VALUES (999), (1000)) AS `v`
    WHERE `v`.`Value` > `p`.`Id`) = 2
""");
    }

    public override async Task Inline_collection_Contains_with_zero_values()
    {
        await base.Inline_collection_Contains_with_zero_values();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE FALSE
""");
    }

    public override async Task Inline_collection_Contains_with_one_value()
    {
        await base.Inline_collection_Contains_with_one_value();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` = 2
""");
    }

    public override async Task Inline_collection_Contains_with_two_values()
    {
        await base.Inline_collection_Contains_with_two_values();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` IN (2, 999)
""");
    }

    public override async Task Inline_collection_Contains_with_three_values()
    {
        await base.Inline_collection_Contains_with_three_values();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` IN (2, 999, 1000)
""");
    }

    public override async Task Inline_collection_Contains_with_all_parameters()
    {
        await base.Inline_collection_Contains_with_all_parameters();

        AssertSql(
            """
@i='2'
@j='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` IN (@i, @j)
""");
    }

    public override async Task Inline_collection_Contains_with_constant_and_parameter()
    {
        await base.Inline_collection_Contains_with_constant_and_parameter();

        AssertSql(
            """
@j='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` IN (2, @j)
""");
    }

    public override async Task Inline_collection_Contains_with_mixed_value_types()
    {
        await base.Inline_collection_Contains_with_mixed_value_types();

        AssertSql(
            """
@i='11'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (999, @i, `p`.`Id`, `p`.`Id` + `p`.`Int`)
""");
    }

    public override async Task Inline_collection_List_Contains_with_mixed_value_types()
    {
        await base.Inline_collection_List_Contains_with_mixed_value_types();

        AssertSql(
            """
@i='11'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (999, @i, `p`.`Id`, `p`.`Id` + `p`.`Int`)
""");
    }

    public override async Task Inline_collection_Contains_as_Any_with_predicate()
    {
        await base.Inline_collection_Contains_as_Any_with_predicate();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` IN (2, 999)
""");
    }

    public override async Task Inline_collection_negated_Contains_as_All()
    {
        await base.Inline_collection_negated_Contains_as_All();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` NOT IN (2, 999)
""");
    }

    public override async Task Inline_collection_Min_with_two_values()
    {
        await base.Inline_collection_Min_with_two_values();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT MIN(`v`.`Value`)
    FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`Int`)) AS `v`) = 30
""");
    }

    public override async Task Inline_collection_List_Min_with_two_values()
    {
        await base.Inline_collection_List_Min_with_two_values();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT MIN(`v`.`Value`)
    FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`Int`)) AS `v`) = 30
""");
    }

    public override async Task Inline_collection_Max_with_two_values()
    {
        await base.Inline_collection_Max_with_two_values();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT MAX(`v`.`Value`)
    FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`Int`)) AS `v`) = 30
""");
    }

    public override async Task Inline_collection_List_Max_with_two_values()
    {
        await base.Inline_collection_List_Max_with_two_values();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT MAX(`v`.`Value`)
    FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`Int`)) AS `v`) = 30
""");
    }

    public override async Task Inline_collection_Min_with_three_values()
    {
        await base.Inline_collection_Min_with_three_values();

        AssertSql(
            """
@i='25'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT MIN(`v`.`Value`)
    FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`Int`), (@i)) AS `v`) = 25
""");
    }

    public override async Task Inline_collection_List_Min_with_three_values()
    {
        await base.Inline_collection_List_Min_with_three_values();

        AssertSql(
            """
@i='25'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT MIN(`v`.`Value`)
    FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`Int`), (@i)) AS `v`) = 25
""");
    }

    public override async Task Inline_collection_Max_with_three_values()
    {
        await base.Inline_collection_Max_with_three_values();

        AssertSql(
            """
@i='35'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT MAX(`v`.`Value`)
    FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`Int`), (@i)) AS `v`) = 35
""");
    }

    public override async Task Inline_collection_List_Max_with_three_values()
    {
        await base.Inline_collection_List_Max_with_three_values();

        AssertSql(
            """
@i='35'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT MAX(`v`.`Value`)
    FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`Int`), (@i)) AS `v`) = 35
""");
    }

    public override async Task Inline_collection_of_nullable_value_type_Min()
    {
        await base.Inline_collection_of_nullable_value_type_Min();

        AssertSql(
            """
@i='25' (Nullable = true)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT MIN(`v`.`Value`)
    FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`Int`), (@i)) AS `v`) = 25
""");
    }

    public override async Task Inline_collection_of_nullable_value_type_Max()
    {
        await base.Inline_collection_of_nullable_value_type_Max();

        AssertSql(
            """
@i='35' (Nullable = true)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT MAX(`v`.`Value`)
    FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`Int`), (@i)) AS `v`) = 35
""");
    }

    public override async Task Inline_collection_of_nullable_value_type_with_null_Min()
    {
        await base.Inline_collection_of_nullable_value_type_with_null_Min();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT MIN(`v`.`Value`)
    FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`NullableInt`), (NULL)) AS `v`) = 30
""");
    }

    public override async Task Inline_collection_of_nullable_value_type_with_null_Max()
    {
        await base.Inline_collection_of_nullable_value_type_with_null_Max();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT MAX(`v`.`Value`)
    FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`NullableInt`), (NULL)) AS `v`) = 30
""");
    }

    public override async Task Inline_collection_with_single_parameter_element_Contains()
    {
        await base.Inline_collection_with_single_parameter_element_Contains();

        AssertSql(
            """
@i='2'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` = @i
""");
    }

    public override async Task Inline_collection_with_single_parameter_element_Count()
    {
        await base.Inline_collection_with_single_parameter_element_Count();

        AssertSql(
            """
@i='2'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT @i AS `Value`) AS `v`
    WHERE `v`.`Value` > `p`.`Id`) = 1
""");
    }

    public override async Task Inline_collection_Contains_with_EF_Parameter()
    {
        await base.Inline_collection_Contains_with_EF_Parameter();

        AssertSql(
            """
@__p_0='[2,999,1000]' (Size = 4000)

SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE [p].[Id] IN (
    SELECT [p0].[value]
    FROM OPENJSON(@__p_0) WITH ([value] int '$') AS [p0]
)
""");
    }

    public override async Task Inline_collection_Contains_with_IEnumerable_EF_Parameter()
    {
        await base.Inline_collection_Contains_with_IEnumerable_EF_Parameter();

        AssertSql(
            """
@Select='["10","a","aa"]' (Size = 4000)

SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[NullableWrappedId], [p].[NullableWrappedIdWithNullableComparer], [p].[String], [p].[Strings], [p].[WrappedId]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE [p].[NullableString] IN (
    SELECT [s].[value]
    FROM OPENJSON(@Select) WITH ([value] nvarchar(max) '$') AS [s]
)
""");
    }

    public override async Task Inline_collection_Count_with_column_predicate_with_EF_Parameter()
    {
        await base.Inline_collection_Count_with_column_predicate_with_EF_Parameter();

        AssertSql(
            """
@__p_0='[2,999,1000]' (Size = 4000)

SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT COUNT(*)
    FROM OPENJSON(@__p_0) WITH ([value] int '$') AS [p0]
    WHERE [p0].[value] > [p].[Id]) = 2
""");
    }

    public override async Task Inline_collection_in_query_filter()
    {
        await base.Inline_collection_in_query_filter();

        AssertSql(
            """
SELECT TOP 2 `t`.`Id`, `t`.`Ints`
FROM `TestEntity` AS `t`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT CLNG(1) AS `Value` UNION ALL VALUES (2), (3)) AS `v`
    WHERE `v`.`Value` > `t`.`Id`) = 1
""");
    }

    public override async Task Inline_collection_SelectMany_with_unreferenced_collection_value()
    {
        await base.Inline_collection_SelectMany_with_unreferenced_collection_value();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
CROSS APPLY (SELECT ('a' & '') AS `Value` UNION ALL VALUES ('b')) AS `v`
""");
    }

    public override async Task Parameter_collection_Count()
    {
        await base.Parameter_collection_Count();

        AssertSql(
            """
@ids1='2'
@ids2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT @ids1 AS `Value` UNION ALL VALUES (@ids2)) AS `i`
    WHERE `i`.`Value` > `p`.`Id`) = 1
""");
    }

    public override async Task Parameter_collection_of_ints_Contains_int()
    {
        await base.Parameter_collection_of_ints_Contains_int();

        AssertSql(
            """
@ints1='10'
@ints2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (@ints1, @ints2)
""",
            //
            """
@ints1='10'
@ints2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` NOT IN (@ints1, @ints2)
""");
    }

    public override async Task Parameter_collection_HashSet_of_ints_Contains_int()
    {
        await base.Parameter_collection_HashSet_of_ints_Contains_int();

        AssertSql(
            """
@ints1='10'
@ints2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (@ints1, @ints2)
""",
            //
            """
@ints1='10'
@ints2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` NOT IN (@ints1, @ints2)
""");
    }

    public override async Task Parameter_collection_FrozenSet_of_ints_Contains_int()
    {
        await base.Parameter_collection_FrozenSet_of_ints_Contains_int();

        AssertSql(
            """
@ints1='10'
@ints2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (@ints1, @ints2)
""",
            //
            """
@ints1='10'
@ints2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` NOT IN (@ints1, @ints2)
""");
    }

    public override async Task Parameter_collection_ImmutableArray_of_ints_Contains_int()
    {
        await base.Parameter_collection_ImmutableArray_of_ints_Contains_int();

        AssertSql(
            """
@ints1='10'
@ints2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (@ints1, @ints2)
""",
            //
            """
@ints1='10'
@ints2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` NOT IN (@ints1, @ints2)
""");
    }

    public override async Task Parameter_collection_IReadOnlySet_of_ints_Contains_int()
    {
        await base.Parameter_collection_IReadOnlySet_of_ints_Contains_int();

        AssertSql(
            """
@ints1='10'
@ints2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (@ints1, @ints2)
""",
            //
            """
@ints1='10'
@ints2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` NOT IN (@ints1, @ints2)
""");
    }

    public override async Task Parameter_collection_ReadOnlyCollectionWithContains_of_ints_Contains_int()
    {
        await base.Parameter_collection_ReadOnlyCollectionWithContains_of_ints_Contains_int();

        AssertSql(
            """
@ints1='10'
@ints2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (@ints1, @ints2)
""",
            //
            """
@ints1='10'
@ints2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` NOT IN (@ints1, @ints2)
""");
    }

    public override async Task Parameter_collection_of_ints_Contains_nullable_int()
    {
        await base.Parameter_collection_of_ints_Contains_nullable_int();

        AssertSql(
            """
@ints1='10'
@ints2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableInt` IN (@ints1, @ints2)
""",
            //
            """
@ints1='10'
@ints2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableInt` NOT IN (@ints1, @ints2) OR `p`.`NullableInt` IS NULL
""");
    }

    public override async Task Parameter_collection_of_nullable_ints_Contains_int()
    {
        await base.Parameter_collection_of_nullable_ints_Contains_int();

        AssertSql(
            """
@nullableInts1='10'
@nullableInts2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (@nullableInts1, @nullableInts2)
""",
            //
            """
@nullableInts1='10'
@nullableInts2='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` NOT IN (@nullableInts1, @nullableInts2)
""");
    }

    public override async Task Parameter_collection_of_nullable_ints_Contains_nullable_int()
    {
        await base.Parameter_collection_of_nullable_ints_Contains_nullable_int();

        AssertSql(
            """
@nullableInts1='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableInt` IS NULL OR `p`.`NullableInt` = @nullableInts1
""",
            //
            """
@nullableInts1='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableInt` IS NOT NULL AND `p`.`NullableInt` <> @nullableInts1
""");
    }

    public override async Task Parameter_collection_of_nullable_ints_Contains_nullable_int_with_EF_Parameter()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(base.Parameter_collection_of_nullable_ints_Contains_nullable_int_with_EF_Parameter);

        AssertSql();
    }

    public override async Task Parameter_collection_of_strings_Contains_string()
    {
        await base.Parameter_collection_of_strings_Contains_string();

        AssertSql(
            """
@strings1='10' (Size = 255)
@strings2='999' (Size = 255)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`String` IN (@strings1, @strings2)
""",
            //
            """
@strings1='10' (Size = 255)
@strings2='999' (Size = 255)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`String` NOT IN (@strings1, @strings2)
""");
    }

    public override async Task Parameter_collection_of_strings_Contains_nullable_string()
    {
        await base.Parameter_collection_of_strings_Contains_nullable_string();

        AssertSql(
            """
@strings1='10' (Size = 255)
@strings2='999' (Size = 255)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableString` IN (@strings1, @strings2)
""",
            //
            """
@strings1='10' (Size = 255)
@strings2='999' (Size = 255)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableString` NOT IN (@strings1, @strings2) OR `p`.`NullableString` IS NULL
""");
    }

    public override async Task Parameter_collection_of_nullable_strings_Contains_string()
    {
        await base.Parameter_collection_of_nullable_strings_Contains_string();

        AssertSql(
            """
@strings1='10' (Size = 255)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`String` = @strings1
""",
            //
            """
@strings1='10' (Size = 255)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`String` <> @strings1
""");
    }

    public override async Task Parameter_collection_of_nullable_strings_Contains_nullable_string()
    {
        await base.Parameter_collection_of_nullable_strings_Contains_nullable_string();

        AssertSql(
            """
@strings1='999' (Size = 255)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableString` IS NULL OR `p`.`NullableString` = @strings1
""",
            //
            """
@strings1='999' (Size = 255)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableString` IS NOT NULL AND `p`.`NullableString` <> @strings1
""");
    }

    public override async Task Parameter_collection_of_DateTimes_Contains()
    {
        await base.Parameter_collection_of_DateTimes_Contains();

        AssertSql(
            """
@dateTimes1='2020-01-10T12:30:00.0000000Z' (DbType = DateTime)
@dateTimes2='9999-01-01T00:00:00.0000000Z' (DbType = DateTime)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`DateTime` IN (@dateTimes1, @dateTimes2)
""");
    }

    public override async Task Parameter_collection_of_bools_Contains()
    {
        await base.Parameter_collection_of_bools_Contains();

        AssertSql(
            """
@bools1='True'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Bool` = @bools1
""");
    }

    public override async Task Parameter_collection_of_enums_Contains()
    {
        await base.Parameter_collection_of_enums_Contains();

        AssertSql(
            """
@enums1='0'
@enums2='3'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Enum` IN (@enums1, @enums2)
""");
    }

    public override async Task Parameter_collection_null_Contains()
    {
        await base.Parameter_collection_null_Contains();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE FALSE
""");
    }

    public override async Task Parameter_collection_empty_Contains()
    {
        await base.Parameter_collection_empty_Contains();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE FALSE
""");
    }

    public override async Task Parameter_collection_empty_Join()
    {
        await base.Parameter_collection_empty_Join();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
INNER JOIN (
    SELECT NULL AS `Value`
    WHERE FALSE
) AS `p0` ON `p`.`Id` = `p0`.`Value`
""");
    }

    public override async Task Parameter_collection_Contains_with_EF_Constant()
    {
        await base.Parameter_collection_Contains_with_EF_Constant();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` IN (2, 999, 1000)
""");
    }

    public override async Task Parameter_collection_Where_with_EF_Constant_Where_Any()
    {
        await base.Parameter_collection_Where_with_EF_Constant_Where_Any();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE EXISTS (
    SELECT 1
    FROM (SELECT CLNG(2) AS `Value` UNION ALL VALUES (999), (1000)) AS `i`
    WHERE `i`.`Value` > 0)
""");
    }

    public override async Task Parameter_collection_Count_with_column_predicate_with_EF_Constant()
    {
        await base.Parameter_collection_Count_with_column_predicate_with_EF_Constant();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT CLNG(2) AS `Value` UNION ALL VALUES (999), (1000)) AS `i`
    WHERE `i`.`Value` > `p`.`Id`) = 2
""");
    }

    public override async Task Parameter_collection_Count_with_huge_number_of_values()
    {
        await base.Parameter_collection_Count_with_huge_number_of_values();

        Assert.Contains("VALUES", Fixture.TestSqlLoggerFactory.SqlStatements[0], StringComparison.Ordinal);
    }

    public override async Task Parameter_collection_Count_with_huge_number_of_values_over_5_operations()
    {
        await base.Parameter_collection_Count_with_huge_number_of_values_over_5_operations();
    }

    public override async Task Parameter_collection_Count_with_huge_number_of_values_over_5_operations_same_parameter()
    {
        await base.Parameter_collection_Count_with_huge_number_of_values_over_5_operations_same_parameter();
    }

    public override async Task Parameter_collection_Count_with_huge_number_of_values_over_2_operations_same_parameter_different_property()
    {
        await base.Parameter_collection_Count_with_huge_number_of_values_over_2_operations_same_parameter_different_property();

        Assert.Contains("OPENJSON(@ids) WITH ([Value] int '$')", Fixture.TestSqlLoggerFactory.SqlStatements[0], StringComparison.Ordinal);
    }

    public override async Task Parameter_collection_Count_with_huge_number_of_values_over_5_operations_forced_constants()
    {
        await base.Parameter_collection_Count_with_huge_number_of_values_over_5_operations_forced_constants();
    }

    public override async Task Parameter_collection_Count_with_huge_number_of_values_over_5_operations_mixed_parameters_constants()
    {
        await base.Parameter_collection_Count_with_huge_number_of_values_over_5_operations_mixed_parameters_constants();
    }

    public override async Task Parameter_collection_of_ints_Contains_int_with_huge_number_of_values()
    {
        await base.Parameter_collection_of_ints_Contains_int_with_huge_number_of_values();

        Assert.DoesNotContain("OPENJSON", Fixture.TestSqlLoggerFactory.SqlStatements[0], StringComparison.Ordinal);
        Assert.DoesNotContain("OPENJSON", Fixture.TestSqlLoggerFactory.SqlStatements[1], StringComparison.Ordinal);
    }

    public override async Task Parameter_collection_of_ints_Contains_int_with_huge_number_of_values_over_5_operations()
    {
        await base.Parameter_collection_of_ints_Contains_int_with_huge_number_of_values_over_5_operations();
    }

    public override async Task Parameter_collection_of_ints_Contains_int_with_huge_number_of_values_over_5_operations_same_parameter()
    {
        await base.Parameter_collection_of_ints_Contains_int_with_huge_number_of_values_over_5_operations_same_parameter();

        Assert.Contains("@ints1=", Fixture.TestSqlLoggerFactory.SqlStatements[0], StringComparison.Ordinal);
        Assert.Contains("@ints2=", Fixture.TestSqlLoggerFactory.SqlStatements[0], StringComparison.Ordinal);
        Assert.Contains("@ints1=", Fixture.TestSqlLoggerFactory.SqlStatements[1], StringComparison.Ordinal);
        Assert.Contains("@ints2=", Fixture.TestSqlLoggerFactory.SqlStatements[1], StringComparison.Ordinal);
    }

    public override async Task
        Parameter_collection_of_ints_Contains_int_with_huge_number_of_values_over_2_operations_same_parameter_different_property()
    {
        await base
            .Parameter_collection_of_ints_Contains_int_with_huge_number_of_values_over_2_operations_same_parameter_different_property();

        Assert.Contains("OPENJSON(@ints) WITH ([Value] int '$')", Fixture.TestSqlLoggerFactory.SqlStatements[0], StringComparison.Ordinal);
        Assert.Contains("OPENJSON(@ints) WITH ([Value] int '$')", Fixture.TestSqlLoggerFactory.SqlStatements[1], StringComparison.Ordinal);
    }

    public override async Task Parameter_collection_of_ints_Contains_int_with_huge_number_of_values_over_5_operations_forced_constants()
    {
        await base.Parameter_collection_of_ints_Contains_int_with_huge_number_of_values_over_5_operations_forced_constants();
    }

    public override async Task Parameter_collection_of_ints_Contains_int_with_huge_number_of_values_over_5_operations_mixed_parameters_constants()
    {
        await base.Parameter_collection_of_ints_Contains_int_with_huge_number_of_values_over_5_operations_mixed_parameters_constants();

        Assert.Contains("OPENJSON(@ints) WITH ([Value] int '$')", Fixture.TestSqlLoggerFactory.SqlStatements[0], StringComparison.Ordinal);
        Assert.Contains("OPENJSON(@ints) WITH ([Value] int '$')", Fixture.TestSqlLoggerFactory.SqlStatements[1], StringComparison.Ordinal);
    }

    public override async Task Parameter_collection_Count_with_column_predicate_with_default_mode(ParameterTranslationMode mode)
    {
        await base.Parameter_collection_Count_with_column_predicate_with_default_mode(mode);

        switch (mode)
        {
            case ParameterTranslationMode.Constant:
            {
                AssertSql(
                    """
SELECT `t`.`Id`
FROM `TestEntity` AS `t`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT CLNG(2) AS `Value` UNION ALL VALUES (999)) AS `i`
    WHERE `i`.`Value` > `t`.`Id`) = 1
""");
                break;
            }

            case ParameterTranslationMode.Parameter:
            {
                AssertSql(
                    """
@ids='[2,999]' (Size = 4000)

SELECT [t].[Id]
FROM [TestEntity] AS [t]
WHERE (
SELECT COUNT(*)
FROM OPENJSON(@ids) WITH ([value] int '$') AS [i]
WHERE [i].[value] > [t].[Id]) = 1
""");

                break;
            }

            case ParameterTranslationMode.MultipleParameters:
            {
                AssertSql(
                    """
@ids1='2'
@ids2='999'

SELECT `t`.`Id`
FROM `TestEntity` AS `t`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT @ids1 AS `Value` UNION ALL VALUES (@ids2)) AS `i`
    WHERE `i`.`Value` > `t`.`Id`) = 1
""");
                break;
            }

            default:
                throw new NotImplementedException();
        }
    }

    public override async Task Parameter_collection_Contains_with_default_mode(ParameterTranslationMode mode)
    {
        await base.Parameter_collection_Contains_with_default_mode(mode);

        switch (mode)
        {
            case ParameterTranslationMode.Constant:
            {
                AssertSql(
                    """
SELECT `t`.`Id`
FROM `TestEntity` AS `t`
WHERE `t`.`Id` IN (2, 999)
""");
                break;
            }

            case ParameterTranslationMode.Parameter:
            {
                AssertSql(
                    """
@ints1='2'
@ints2='999'

SELECT `t`.`Id`
FROM `TestEntity` AS `t`
WHERE `t`.`Id` IN (@ints1, @ints2)
""");

                break;
            }

            case ParameterTranslationMode.MultipleParameters:
            {
                AssertSql(
                    """
@ints1='2'
@ints2='999'

SELECT `t`.`Id`
FROM `TestEntity` AS `t`
WHERE `t`.`Id` IN (@ints1, @ints2)
""");
                break;
            }

            default:
                throw new NotImplementedException();
        }
    }

    public override async Task Parameter_collection_Count_with_column_predicate_with_default_mode_EF_Constant(ParameterTranslationMode mode)
    {
        await base.Parameter_collection_Count_with_column_predicate_with_default_mode_EF_Constant(mode);

        AssertSql(
            """
SELECT `t`.`Id`
FROM `TestEntity` AS `t`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT CLNG(2) AS `Value` UNION ALL VALUES (999)) AS `i`
    WHERE `i`.`Value` > `t`.`Id`) = 1
""");
    }

    public override async Task Parameter_collection_Contains_with_default_mode_EF_Constant(ParameterTranslationMode mode)
    {
        await base.Parameter_collection_Contains_with_default_mode_EF_Constant(mode);

        AssertSql(
            """
SELECT `t`.`Id`
FROM `TestEntity` AS `t`
WHERE `t`.`Id` IN (2, 999)
""");
    }

    public override async Task Parameter_collection_Count_with_column_predicate_with_default_mode_EF_Parameter(
        ParameterTranslationMode mode)
    {
        await base.Parameter_collection_Count_with_column_predicate_with_default_mode_EF_Parameter(mode);

        AssertSql(
            """
@ids='[2,999]' (Size = 4000)

SELECT [t].[Id]
FROM [TestEntity] AS [t]
WHERE (
SELECT COUNT(*)
FROM OPENJSON(@ids) WITH ([value] int '$') AS [i]
WHERE [i].[value] > [t].[Id]) = 1
""");
    }

    public override async Task Parameter_collection_Contains_with_default_mode_EF_Parameter(ParameterTranslationMode mode)
    {
        await base.Parameter_collection_Contains_with_default_mode_EF_Parameter(mode);

        AssertSql(
            """
@ints='[2,999]' (Size = 4000)

SELECT [t].[Id]
FROM [TestEntity] AS [t]
WHERE [t].[Id] IN (
SELECT [i].[value]
FROM OPENJSON(@ints) WITH ([value] int '$') AS [i]
)
""");
    }

    public override async Task Parameter_collection_Count_with_column_predicate_with_default_mode_EF_MultipleParameters(
        ParameterTranslationMode mode)
    {
        await base.Parameter_collection_Count_with_column_predicate_with_default_mode_EF_MultipleParameters(mode);

        AssertSql(
            """
@ids1='2'
@ids2='999'

SELECT `t`.`Id`
FROM `TestEntity` AS `t`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT @ids1 AS `Value` UNION ALL VALUES (@ids2)) AS `i`
    WHERE `i`.`Value` > `t`.`Id`) = 1
""");
    }

    public override async Task Parameter_collection_Contains_with_default_mode_EF_MultipleParameters(ParameterTranslationMode mode)
    {
        await base.Parameter_collection_Contains_with_default_mode_EF_MultipleParameters(mode);

        AssertSql(
            """
@ints1='2'
@ints2='999'

SELECT `t`.`Id`
FROM `TestEntity` AS `t`
WHERE `t`.`Id` IN (@ints1, @ints2)
""");
    }

    public override async Task Parameter_collection_Contains_parameter_bucketization()
    {
        await base.Parameter_collection_Contains_parameter_bucketization();

        AssertSql(
            """
@ints1='2'
@ints2='999'
@ints3='2'
@ints4='2'
@ints5='2'
@ints6='2'
@ints7='2'
@ints8='2'
@ints9='2'
@ints10='2'
@ints11='2'
@ints12='2'
@ints13='2'
@ints14='2'
@ints15='2'
@ints16='2'
@ints17='2'
@ints18='2'
@ints19='2'
@ints20='2'

SELECT `t`.`Id`
FROM `TestEntity` AS `t`
WHERE `t`.`Id` IN (@ints1, @ints2, @ints3, @ints4, @ints5, @ints6, @ints7, @ints8, @ints9, @ints10, @ints11, @ints12, @ints13, @ints14, @ints15, @ints16, @ints17, @ints18, @ints19, @ints20)
""");
    }

    public override async Task Parameter_collection_of_enum_Cast_from_different_enum_type(ParameterTranslationMode mode)
    {
        await base.Parameter_collection_of_enum_Cast_from_different_enum_type(mode);

        switch (mode)
        {
            case ParameterTranslationMode.Constant:
            {
                AssertSql(
                    """
SELECT `t`.`Id`
FROM `TestEntity38008` AS `t`
WHERE EXISTS (
    SELECT 1
    FROM (SELECT 2 AS `Value`) AS `f`
    WHERE `f`.`Value` = `t`.`Status`)
""");
                break;
            }

            case ParameterTranslationMode.Parameter:
            {
                AssertSql(
                    """
@filter='[2]' (Size = 4000)

SELECT [t].[Id]
FROM [TestEntity38008] AS [t]
WHERE EXISTS (
SELECT 1
FROM OPENJSON(@filter) WITH ([value] int '$') AS [f]
WHERE [f].[value] = [t].[Status])
""");

                break;
            }

            case ParameterTranslationMode.MultipleParameters:
            {
                AssertSql(
                    """
@filter1='2'

SELECT `t`.`Id`
FROM `TestEntity38008` AS `t`
WHERE EXISTS (
    SELECT 1
    FROM (SELECT @filter1 AS `Value`) AS `f`
    WHERE `f`.`Value` = `t`.`Status`)
""");
                break;
            }

            default:
                throw new NotImplementedException();
        }
    }

    public override async Task Static_readonly_collection_List_of_ints_Contains_int()
    {
        await base.Static_readonly_collection_List_of_ints_Contains_int();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (10, 999)
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` NOT IN (10, 999)
""");
    }

    public override async Task Static_readonly_collection_FrozenSet_of_ints_Contains_int()
    {
        await base.Static_readonly_collection_FrozenSet_of_ints_Contains_int();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (10, 999)
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` NOT IN (10, 999)
""");
    }

    public override async Task Static_readonly_collection_ImmutableArray_of_ints_Contains_int()
    {
        await base.Static_readonly_collection_ImmutableArray_of_ints_Contains_int();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (10, 999)
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` NOT IN (10, 999)
""");
    }

    public override Task Column_collection_of_ints_Contains()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_of_ints_Contains(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_of_nullable_ints_Contains()
        => AssertTranslationFailed(() => base.Column_collection_of_nullable_ints_Contains());

    public override Task Column_collection_of_nullable_ints_Contains_null()
        => AssertTranslationFailed(() => base.Column_collection_of_nullable_ints_Contains_null());

    [Fact]
    public virtual async Task Json_representation_of_bool_array()
    {
        await using var context = CreateContext();

        Assert.Equal(
            "[true,false]",
            await context.Database.SqlQuery<string>($"SELECT [Bools] AS [Value] FROM [PrimitiveCollectionsEntity] WHERE [Id] = 1").SingleAsync());
    }

    public override async Task Column_collection_of_strings_Contains()
    {
        await base.Column_collection_of_strings_Contains();

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[NullableWrappedId], [p].[NullableWrappedIdWithNullableComparer], [p].[String], [p].[Strings], [p].[WrappedId]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE N'10' IN (
SELECT [s].[value]
FROM OPENJSON([p].[Strings]) WITH ([value] nvarchar(max) '$') AS [s]
)
""");
    }

    public override async Task Column_collection_of_strings_Contains_null()
    {
        await base.Column_collection_of_strings_Contains_null();

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[NullableWrappedId], [p].[NullableWrappedIdWithNullableComparer], [p].[String], [p].[Strings], [p].[WrappedId]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE 0 = 1
""");
    }

    public override Task Column_collection_of_nullable_strings_contains_null()
        => AssertTranslationFailedWithDetails(
            () => base.Column_collection_of_nullable_strings_contains_null(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_of_bools_Contains()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_of_bools_Contains(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override async Task Column_with_custom_converter()
    {
        await base.Column_with_custom_converter();

        AssertSql(
            """
@ints='1,2,3' (Size = 255)

SELECT TOP 2 `t`.`Id`, `t`.`Ints`
FROM `TestEntity` AS `t`
WHERE `t`.`Ints` = @ints
""");
    }

    public override async Task Column_collection_inside_json_owned_entity()
    {
        await base.Column_collection_inside_json_owned_entity();

        AssertSql(
            """
SELECT TOP(2) [t].[Id], [t].[Owned]
FROM [TestOwner] AS [t]
WHERE (
SELECT COUNT(*)
FROM OPENJSON(JSON_QUERY([t].[Owned], '$.Strings')) AS [s]) = 2
""",
            //
            """
SELECT TOP(2) [t].[Id], [t].[Owned]
FROM [TestOwner] AS [t]
WHERE JSON_VALUE([t].[Owned], '$.Strings[1]') = N'bar'
""");
    }

    public override async Task Parameter_with_inferred_value_converter()
    {
        await base.Parameter_with_inferred_value_converter();

        AssertSql("");
    }

    public override async Task Constant_with_inferred_value_converter()
    {
        await base.Constant_with_inferred_value_converter();

        AssertSql(
            """
SELECT TOP 2 `t`.`Id`, `t`.`Ints`, `t`.`PropertyWithValueConverter`
FROM `TestEntity` AS `t`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT 1 AS `Value` UNION ALL VALUES (8)) AS `v`
    WHERE `v`.`Value` = `t`.`PropertyWithValueConverter`) = 1
""");
    }

    public override Task Multidimensional_array_is_not_supported()
        => base.Multidimensional_array_is_not_supported();

    public override async Task Contains_on_Enumerable()
    {
        await base.Contains_on_Enumerable();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (10, 999)
""");
    }

    public override async Task Contains_on_MemoryExtensions()
    {
        await base.Contains_on_MemoryExtensions();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (10, 999)
""");
    }

    public override async Task Contains_with_MemoryExtensions_with_null_comparer()
    {
        await base.Contains_with_MemoryExtensions_with_null_comparer();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (10, 999)
""");
    }

    public override async Task Min_on_MemoryExtensions()
    {
        await base.Min_on_MemoryExtensions();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT MIN(`v`.`Value`)
    FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`Int`)) AS `v`) = 30
""");
    }

    public override async Task Max_on_MemoryExtensions()
    {
        await base.Max_on_MemoryExtensions();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT MAX(`v`.`Value`)
    FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`Int`)) AS `v`) = 30
""");
    }

    public override Task Column_collection_Count_method()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Count_method(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Length()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Length(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Count_with_predicate()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Count_with_predicate(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Where_Count()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Where_Count(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_index_int()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_index_int(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_index_string()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_index_string(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_index_datetime()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_index_datetime(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_index_beyond_end()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_index_beyond_end(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Nullable_reference_column_collection_index_equals_nullable_column()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_index_beyond_end(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Non_nullable_reference_column_collection_index_equals_nullable_column()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_index_beyond_end(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override async Task Inline_collection_index_Column()
    {
        await base.Inline_collection_index_Column();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT `v`.`Value`
    FROM (SELECT 0 AS `_ord`, CLNG(1) AS `Value` UNION ALL VALUES (1, 2), (2, 3)) AS `v`
    ORDER BY `v`.`_ord`
    OFFSET `p`.`Int` ROWS FETCH NEXT 1 ROWS ONLY) = 1
""");
    }

    public override async Task Inline_collection_index_Column_with_EF_Constant()
    {
        await base.Inline_collection_index_Column_with_EF_Constant();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT `i`.`Value`
    FROM (SELECT 0 AS `_ord`, CLNG(1) AS `Value` UNION ALL VALUES (1, 2), (2, 3)) AS `i`
    ORDER BY `i`.`_ord`
    OFFSET `p`.`Int` ROWS FETCH NEXT 1 ROWS ONLY) = 1
""");
    }

    public override async Task Inline_collection_value_index_Column()
    {
        await base.Inline_collection_value_index_Column();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT `v`.`Value`
    FROM (SELECT 0 AS `_ord`, CLNG(1) AS `Value` UNION ALL VALUES (1, `p`.`Int`), (2, 3)) AS `v`
    ORDER BY `v`.`_ord`
    OFFSET `p`.`Int` ROWS FETCH NEXT 1 ROWS ONLY) = 1
""");
    }

    public override async Task Inline_collection_List_value_index_Column()
    {
        await base.Inline_collection_List_value_index_Column();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT `v`.`Value`
    FROM (SELECT 0 AS `_ord`, CLNG(1) AS `Value` UNION ALL VALUES (1, `p`.`Int`), (2, 3)) AS `v`
    ORDER BY `v`.`_ord`
    OFFSET `p`.`Int` ROWS FETCH NEXT 1 ROWS ONLY) = 1
""");
    }

    public override async Task Parameter_collection_index_Column_equal_Column()
    {
        await base.Parameter_collection_index_Column_equal_Column();
    }

    public override async Task Parameter_collection_index_Column_equal_constant()
    {
        await base.Parameter_collection_index_Column_equal_constant();
    }

    public override Task Column_collection_ElementAt()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_ElementAt(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_First()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_First(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_FirstOrDefault()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_FirstOrDefault(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Single()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Single(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_SingleOrDefault()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_SingleOrDefault(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Skip()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Skip(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Take()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Take(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Skip_Take()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Skip_Take(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Where_Skip()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Where_Skip(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Where_Take()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Where_Take(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Where_Skip_Take()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Where_Skip_Take(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Contains_over_subquery()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Skip_Take(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_OrderByDescending_ElementAt()
        => AssertTranslationFailed(() => base.Column_collection_OrderByDescending_ElementAt());

    public override Task Column_collection_Where_ElementAt()
        => AssertTranslationFailed(() => base.Column_collection_Where_ElementAt());

    public override Task Column_collection_Any()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Any(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Distinct()
        => AssertTranslationFailed(() => base.Column_collection_Distinct());

    public override Task Column_collection_SelectMany()
        => AssertTranslationFailed(() => base.Column_collection_SelectMany());

    public override Task Column_collection_SelectMany_with_filter()
        => AssertTranslationFailed(() => base.Column_collection_SelectMany_with_filter());

    public override Task Column_collection_SelectMany_with_Select_to_anonymous_type()
        => AssertTranslationFailed(() => base.Column_collection_SelectMany_with_Select_to_anonymous_type());

    public override async Task Column_collection_projection_from_top_level()
    {
        await base.Column_collection_projection_from_top_level();

        AssertSql(
            """
SELECT `p`.`Ints`
FROM `PrimitiveCollectionsEntity` AS `p`
ORDER BY `p`.`Id`
""");
    }

    public override Task Column_collection_Join_parameter_collection()
        => AssertTranslationFailed(() => base.Column_collection_Join_parameter_collection());

    public override Task Inline_collection_Join_ordered_column_collection()
        => AssertTranslationFailedWithDetails(() => base.Inline_collection_Join_ordered_column_collection(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Parameter_collection_Concat_column_collection()
        => AssertTranslationFailedWithDetails(() => base.Parameter_collection_Concat_column_collection(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Parameter_collection_with_type_inference_for_JsonScalarExpression()
        => AssertTranslationFailed(() => base.Parameter_collection_Concat_column_collection());

    public override Task Column_collection_Union_parameter_collection()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Union_parameter_collection(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Intersect_inline_collection()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Intersect_inline_collection(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Inline_collection_Except_column_collection()
        => AssertTranslationFailedWithDetails(() => base.Inline_collection_Except_column_collection(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Where_Union()
        => AssertTranslationFailedWithDetails(() => base.Inline_collection_Except_column_collection(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override async Task Column_collection_equality_parameter_collection()
    {
        await base.Column_collection_equality_parameter_collection();

        AssertSql(
            """
@ints='[1,10]' (Size = 255)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Ints` = @ints
""");
    }

    public override async Task Column_collection_Concat_parameter_collection_equality_inline_collection()
    {
        await base.Column_collection_Concat_parameter_collection_equality_inline_collection();

        AssertSql();
    }

    public override async Task Column_collection_equality_inline_collection()
    {
        await base.Column_collection_equality_inline_collection();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Ints` = '[1,10]'
""");
    }

    public override async Task Column_collection_equality_inline_collection_with_parameters()
    {
        await base.Column_collection_equality_inline_collection_with_parameters();

        AssertSql();
    }

    public override async Task Column_collection_Where_equality_inline_collection()
    {
        await base.Column_collection_Where_equality_inline_collection();

        AssertSql();
    }

    public override Task Parameter_collection_in_subquery_Union_column_collection_as_compiled_query()
        => AssertTranslationFailedWithDetails(() => base.Parameter_collection_in_subquery_Union_column_collection_as_compiled_query(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Parameter_collection_in_subquery_Union_column_collection()
        => AssertTranslationFailedWithDetails(() => base.Parameter_collection_in_subquery_Union_column_collection(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Parameter_collection_in_subquery_Union_column_collection_nested()
        => AssertTranslationFailedWithDetails(() => base.Parameter_collection_in_subquery_Union_column_collection_nested(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override void Parameter_collection_in_subquery_and_Convert_as_compiled_query()
    {
        // Base implementation asserts that a different exception is thrown
    }

    public override async Task Parameter_collection_in_subquery_Count_as_compiled_query()
    {
        await base.Parameter_collection_in_subquery_Count_as_compiled_query();

        AssertSql(
            """
@ints1='10'
@ints2='111'

SELECT COUNT(*)
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (
        SELECT `i`.`Value` AS `Value0`
        FROM (SELECT 0 AS `_ord`, @ints1 AS `Value` UNION ALL VALUES (1, @ints2)) AS `i`
        ORDER BY `i`.`_ord`
        OFFSET 1 ROWS
    ) AS `i0`
    WHERE `i0`.`Value0` > `p`.`Id`) = 1
""");
    }

    public override async Task Parameter_collection_in_subquery_Union_another_parameter_collection_as_compiled_query()
    {
        await base.Parameter_collection_in_subquery_Union_another_parameter_collection_as_compiled_query();

        AssertSql();
    }

    public override async Task Compiled_query_with_uncorrelated_parameter_collection_expression()
    {
        await base.Compiled_query_with_uncorrelated_parameter_collection_expression();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE EXISTS (
    SELECT 1
    FROM (
        SELECT NULL AS `Value`
        WHERE FALSE
    ) AS `i`)
""");
    }

    public override Task Column_collection_in_subquery_Union_parameter_collection()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_in_subquery_Union_parameter_collection(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override async Task Project_collection_of_ints_simple()
    {
        await base.Project_collection_of_ints_simple();

        AssertSql(
            """
SELECT `p`.`Ints`
FROM `PrimitiveCollectionsEntity` AS `p`
ORDER BY `p`.`Id`
""");
    }

    public override Task Project_collection_of_ints_ordered()
        // we don't propagate error details from projection
        => AssertTranslationFailed(() => base.Project_collection_of_ints_ordered());

    public override Task Project_collection_of_datetimes_filtered()
        // we don't propagate error details from projection
        => AssertTranslationFailed(() => base.Project_collection_of_datetimes_filtered());

    public override async Task Project_collection_of_nullable_ints_with_paging()
    {
        await base.Project_collection_of_nullable_ints_with_paging();

        // client eval
        AssertSql(
            """
SELECT `p`.`NullableInts`
FROM `PrimitiveCollectionsEntity` AS `p`
ORDER BY `p`.`Id`
""");
    }

    public override Task Project_collection_of_nullable_ints_with_paging2()
        // we don't propagate error details from projection
        => AssertTranslationFailed(() => base.Project_collection_of_nullable_ints_with_paging2());

    public override async Task Project_collection_of_nullable_ints_with_paging3()
    {
        await base.Project_collection_of_nullable_ints_with_paging3();

        // client eval
        AssertSql(
            """
SELECT `p`.`NullableInts`
FROM `PrimitiveCollectionsEntity` AS `p`
ORDER BY `p`.`Id`
""");
    }

    public override async Task Project_collection_of_ints_with_distinct()
    {
        await base.Project_collection_of_ints_with_distinct();

        // client eval
        AssertSql(
            """
SELECT `p`.`Ints`
FROM `PrimitiveCollectionsEntity` AS `p`
ORDER BY `p`.`Id`
""");
    }

    public override async Task Project_collection_of_nullable_ints_with_distinct()
    {
        await base.Project_collection_of_nullable_ints_with_distinct();

        AssertSql("");
    }

    public override async Task Project_collection_of_ints_with_ToList_and_FirstOrDefault()
    {
        await base.Project_collection_of_ints_with_ToList_and_FirstOrDefault();

        AssertSql(
            """
SELECT TOP 1 `p`.`Ints`
FROM `PrimitiveCollectionsEntity` AS `p`
ORDER BY `p`.`Id`
""");
    }

    public override Task Project_empty_collection_of_nullables_and_collection_only_containing_nulls()
        // we don't propagate error details from projection
        => AssertTranslationFailed(() => base.Project_empty_collection_of_nullables_and_collection_only_containing_nulls());

    public override Task Project_multiple_collections()
        // we don't propagate error details from projection
        => AssertTranslationFailed(() => base.Project_multiple_collections());

    public override async Task Project_primitive_collections_element()
    {
        await base.Project_primitive_collections_element();

        AssertSql(
            """
SELECT `p`.`Ints`, `p`.`DateTimes`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` < 4
ORDER BY `p`.`Id`
""");
    }

    public override async Task Project_inline_collection()
    {
        await base.Project_inline_collection();

        AssertSql(
            """
SELECT `p`.`String`
FROM `PrimitiveCollectionsEntity` AS `p`
""");
    }

    public override async Task Project_inline_collection_with_Union()
    {
        await base.Project_inline_collection_with_Union();

        AssertSql(
            """
SELECT `p`.`Id`, `u`.`Value`
FROM `PrimitiveCollectionsEntity` AS `p`
OUTER APPLY (
    SELECT `v`.`Value`
    FROM (SELECT `p`.`String` AS `Value`) AS `v`
    UNION
    SELECT `p0`.`String` AS `Value`
    FROM `PrimitiveCollectionsEntity` AS `p0`
) AS `u`
ORDER BY `p`.`Id`
""");
    }

    public override async Task Project_inline_collection_with_Concat()
    {
        await base.Project_inline_collection_with_Concat();

        AssertSql();
    }

    public override async Task Project_collection_from_entity_type_with_owned()
    {
        await base.Project_collection_from_entity_type_with_owned();

        AssertSql(
            """
SELECT `t`.`Ints`
FROM `TestEntityWithOwned` AS `t`
""");
    }

    public override async Task Subquery_over_primitive_collection_on_inheritance_derived_type()
    {
        await base.Subquery_over_primitive_collection_on_inheritance_derived_type();

        AssertSql(
            """
SELECT [b].[Id], [b].[Discriminator], [b].[Ints]
FROM [BaseType] AS [b]
WHERE EXISTS (
    SELECT 1
    FROM OPENJSON([b].[Ints]) AS [i])
""");
    }

    public override async Task Nested_contains_with_Lists_and_no_inferred_type_mapping()
    {
        await base.Nested_contains_with_Lists_and_no_inferred_type_mapping();

        AssertSql(
            """
@ints1='1'
@ints2='2'
@ints3='3'
@strings1='one' (Size = 255)
@strings2='two' (Size = 255)
@strings3='three' (Size = 255)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE CASE
    WHEN `p`.`Int` IN (@ints1, @ints2, @ints3) THEN 'one'
    ELSE 'two'
END IN (@strings1, @strings2, @strings3)
""");
    }

    public override async Task Nested_contains_with_arrays_and_no_inferred_type_mapping()
    {
        await base.Nested_contains_with_arrays_and_no_inferred_type_mapping();

        AssertSql(
            """
@ints1='1'
@ints2='2'
@ints3='3'
@strings1='one' (Size = 255)
@strings2='two' (Size = 255)
@strings3='three' (Size = 255)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE CASE
    WHEN `p`.`Int` IN (@ints1, @ints2, @ints3) THEN 'one'
    ELSE 'two'
END IN (@strings1, @strings2, @strings3)
""");
    }

    public override async Task Parameter_collection_of_structs_Contains_struct()
    {
        await base.Parameter_collection_of_structs_Contains_struct();

        AssertSql(
            """
@values1='22'
@values2='33'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`WrappedId` IN (@values1, @values2)
""",
            //
            """
@values1='11'
@values2='44'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`WrappedId` NOT IN (@values1, @values2)
""");
    }

    public override async Task Parameter_collection_of_structs_Contains_nullable_struct()
    {
        await base.Parameter_collection_of_structs_Contains_nullable_struct();

        AssertSql(
            """
@values1='22'
@values2='33'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedId` IN (@values1, @values2)
""",
            //
            """
@values1='11'
@values2='44'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedId` NOT IN (@values1, @values2) OR `p`.`NullableWrappedId` IS NULL
""");
    }

    public override async Task Parameter_collection_of_structs_Contains_nullable_struct_with_nullable_comparer()
    {
        await base.Parameter_collection_of_structs_Contains_nullable_struct_with_nullable_comparer();

        AssertSql(
            """
@values1='22'
@values2='33'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedIdWithNullableComparer` IN (@values1, @values2)
""",
            //
            """
@values1='11'
@values2='44'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedId` NOT IN (@values1, @values2) OR `p`.`NullableWrappedId` IS NULL
""");
    }

    public override async Task Parameter_collection_of_nullable_structs_Contains_struct()
    {
        await base.Parameter_collection_of_nullable_structs_Contains_struct();

        AssertSql(
            """
@values1='22'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`WrappedId` = @values1
""",
            //
            """
@values1='11'
@values2='44'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`WrappedId` NOT IN (@values1, @values2)
""");
    }

    public override async Task Parameter_collection_of_nullable_structs_Contains_nullable_struct()
    {
        await base.Parameter_collection_of_nullable_structs_Contains_nullable_struct();

        AssertSql(
            """
@values1='22'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedId` IS NULL OR `p`.`NullableWrappedId` = @values1
""",
            //
            """
@values1='11'
@values2='44'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedId` NOT IN (@values1, @values2) OR `p`.`NullableWrappedId` IS NULL
""");
    }

    public override async Task Parameter_collection_of_nullable_structs_Contains_nullable_struct_with_nullable_comparer()
    {
        await base.Parameter_collection_of_nullable_structs_Contains_nullable_struct_with_nullable_comparer();

        AssertSql(
            """
@values1='22'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedIdWithNullableComparer` IS NULL OR `p`.`NullableWrappedIdWithNullableComparer` = @values1
""",
            //
            """
@values1='11'
@values2='44'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedIdWithNullableComparer` NOT IN (@values1, @values2) OR `p`.`NullableWrappedIdWithNullableComparer` IS NULL
""");
    }

    public override async Task Values_of_enum_casted_to_underlying_value()
    {
        await base.Values_of_enum_casted_to_underlying_value();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT CLNG(0) AS `Value` UNION ALL VALUES (1), (2), (3)) AS `v`
    WHERE `v`.`Value` = `p`.`Int`) > 0
""");
    }

    protected override DbContextOptionsBuilder SetParameterizedCollectionMode(DbContextOptionsBuilder optionsBuilder,
        ParameterTranslationMode parameterizedCollectionMode)
    {
        // LibRed's builder, not Jet's: the two write to different options extensions, and adding Jet's to a
        // context already configured for LibRed reads as two relational providers on one context.
        new LibRedDbContextOptionsBuilder(optionsBuilder).UseParameterizedCollectionMode(parameterizedCollectionMode);

        return optionsBuilder;
    }

    [Fact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

    private PrimitiveCollectionsContext CreateContext()
        => Fixture.CreateContext();

    public class PrimitiveCollectionsQueryLibRedFixture : PrimitiveCollectionsQueryFixtureBase, ITestSqlLoggerFactory
    {
        protected override string StoreName
            => "PrimitiveCollectionsTest";

        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override ITestStoreFactory TestStoreFactory
            => LibRedTestStoreFactory.Instance;
    }
}
