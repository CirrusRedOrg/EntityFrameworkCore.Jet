// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

/// <summary>
///     Runs all primitive collection tests with SQL Server compatibility level 120 (SQL Server 2014), which doesn't support OPENJSON.
///     This exercises the older translation paths for e.g. Contains, to make sure things work for providers with no queryable constant/
///     parameter support.
/// </summary>
public class PrimitiveCollectionsQueryJetTest : PrimitiveCollectionsQueryRelationalTestBase<
    PrimitiveCollectionsQueryJetTest.PrimitiveCollectionsQueryJetFixture>
{
    public PrimitiveCollectionsQueryJetTest(PrimitiveCollectionsQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task Inline_collection_of_ints_Contains(bool async)
    {
        await base.Inline_collection_of_ints_Contains(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (10, 999)
""");
    }

    public override async Task Inline_collection_of_nullable_ints_Contains(bool async)
    {
        await base.Inline_collection_of_nullable_ints_Contains(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableInt` IN (10, 999)
""");
    }

    public override async Task Inline_collection_of_nullable_ints_Contains_null(bool async)
    {
        await base.Inline_collection_of_nullable_ints_Contains_null(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableInt` IS NULL OR `p`.`NullableInt` = 999
""");
    }
    public override async Task Inline_collection_Count_with_zero_values(bool async)
    {
        await base.Inline_collection_Count_with_zero_values(async);

        AssertSql();
    }

    public override async Task Inline_collection_Count_with_one_value(bool async)
    {
        await base.Inline_collection_Count_with_one_value(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT CLNG(2) AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_0`) AS `v`
    WHERE `v`.`Value` > `p`.`Id`) = 1
""");
    }

    public override async Task Inline_collection_Count_with_two_values(bool async)
    {
        await base.Inline_collection_Count_with_two_values(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT CLNG(2) AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_0`
    UNION
    SELECT 999 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_1`) AS `v`
    WHERE `v`.`Value` > `p`.`Id`) = 1
""");
    }

    public override async Task Inline_collection_Count_with_three_values(bool async)
    {
        await base.Inline_collection_Count_with_three_values(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT CLNG(2) AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_0`
    UNION
    SELECT 999 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_1`
    UNION
    SELECT 1000 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_2`) AS `v`
    WHERE `v`.`Value` > `p`.`Id`) = 2
""");
    }

    public override async Task Inline_collection_Contains_with_zero_values(bool async)
    {
        await base.Inline_collection_Contains_with_zero_values(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE 0 = 1
""");
    }

    public override async Task Inline_collection_Contains_with_one_value(bool async)
    {
        await base.Inline_collection_Contains_with_one_value(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` = 2
""");
    }

    public override async Task Inline_collection_Contains_with_two_values(bool async)
    {
        await base.Inline_collection_Contains_with_two_values(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` IN (2, 999)
""");
    }

    public override async Task Inline_collection_Contains_with_three_values(bool async)
    {
        await base.Inline_collection_Contains_with_three_values(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` IN (2, 999, 1000)
""");
    }

    public override async Task Inline_collection_Contains_with_all_parameters(bool async)
    {
        await base.Inline_collection_Contains_with_all_parameters(async);

        AssertSql(
            $"""
@__i_0='2'
@__j_1='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` IN ({AssertSqlHelper.Parameter("@__i_0")}, {AssertSqlHelper.Parameter("@__j_1")})
""");
    }

    public override async Task Inline_collection_Contains_with_constant_and_parameter(bool async)
    {
        await base.Inline_collection_Contains_with_constant_and_parameter(async);

        AssertSql(
            $"""
@__j_0='999'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` IN (2, {AssertSqlHelper.Parameter("@__j_0")})
""");
    }

    public override async Task Inline_collection_Contains_with_mixed_value_types(bool async)
    {
        await base.Inline_collection_Contains_with_mixed_value_types(async);

        AssertSql(
            $"""
@__i_0='11'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (999, {AssertSqlHelper.Parameter("@__i_0")}, `p`.`Id`, `p`.`Id` + `p`.`Int`)
""");
    }

    public override async Task Inline_collection_List_Contains_with_mixed_value_types(bool async)
    {
        await base.Inline_collection_List_Contains_with_mixed_value_types(async);

        AssertSql(
            """
@__i_0='11'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (999, @__i_0, `p`.`Id`, `p`.`Id` + `p`.`Int`)
""");
    }

    public override async Task Inline_collection_Contains_as_Any_with_predicate(bool async)
    {
        await base.Inline_collection_Contains_as_Any_with_predicate(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` IN (2, 999)
""");
    }

    public override async Task Inline_collection_negated_Contains_as_All(bool async)
    {
        await base.Inline_collection_negated_Contains_as_All(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` NOT IN (2, 999)
""");
    }

    public override async Task Inline_collection_Min_with_two_values(bool async)
    {
        await base.Inline_collection_Min_with_two_values(async);

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MIN([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[Int])) AS [v]([Value])) = 30
""");
    }

    public override async Task Inline_collection_List_Min_with_two_values(bool async)
    {
        await base.Inline_collection_List_Min_with_two_values(async);

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MIN([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[Int])) AS [v]([Value])) = 30
""");
    }

    public override async Task Inline_collection_Max_with_two_values(bool async)
    {
        await base.Inline_collection_Max_with_two_values(async);

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MAX([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[Int])) AS [v]([Value])) = 30
""");
    }

    public override async Task Inline_collection_List_Max_with_two_values(bool async)
    {
        await base.Inline_collection_List_Max_with_two_values(async);

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MAX([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[Int])) AS [v]([Value])) = 30
""");
    }

    public override async Task Inline_collection_Min_with_three_values(bool async)
    {
        await base.Inline_collection_Min_with_three_values(async);

        AssertSql(
            """
@__i_0='25'

SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MIN([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[Int]), (@__i_0)) AS [v]([Value])) = 25
""");
    }

    public override async Task Inline_collection_List_Min_with_three_values(bool async)
    {
        await base.Inline_collection_List_Min_with_three_values(async);

        AssertSql(
            """
@__i_0='25'

SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MIN([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[Int]), (@__i_0)) AS [v]([Value])) = 25
""");
    }

    public override async Task Inline_collection_Max_with_three_values(bool async)
    {
        await base.Inline_collection_Max_with_three_values(async);

        AssertSql(
            """
@__i_0='35'

SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MAX([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[Int]), (@__i_0)) AS [v]([Value])) = 35
""");
    }

    public override async Task Inline_collection_List_Max_with_three_values(bool async)
    {
        await base.Inline_collection_List_Max_with_three_values(async);

        AssertSql(
            """
@__i_0='35'

SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MAX([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[Int]), (@__i_0)) AS [v]([Value])) = 35
""");
    }

    public override async Task Inline_collection_of_nullable_value_type_Min(bool async)
    {
        await base.Inline_collection_of_nullable_value_type_Min(async);

        AssertSql(
            """
@__i_0='25' (Nullable = true)

SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MIN([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[Int]), (@__i_0)) AS [v]([Value])) = 25
""");
    }

    public override async Task Inline_collection_of_nullable_value_type_Max(bool async)
    {
        await base.Inline_collection_of_nullable_value_type_Max(async);

        AssertSql(
            """
@__i_0='35' (Nullable = true)

SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MAX([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[Int]), (@__i_0)) AS [v]([Value])) = 35
""");
    }

    public override async Task Inline_collection_of_nullable_value_type_with_null_Min(bool async)
    {
        await base.Inline_collection_of_nullable_value_type_with_null_Min(async);

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MIN([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[NullableInt]), (NULL)) AS [v]([Value])) = 30
""");
    }

    public override async Task Inline_collection_of_nullable_value_type_with_null_Max(bool async)
    {
        await base.Inline_collection_of_nullable_value_type_with_null_Max(async);

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MAX([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[NullableInt]), (NULL)) AS [v]([Value])) = 30
""");
    }

    public override async Task Inline_collection_with_single_parameter_element_Contains(bool async)
    {
        await base.Inline_collection_with_single_parameter_element_Contains(async);

        AssertSql(
            """
@__i_0='2'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` = @__i_0
""");
    }

    public override async Task Inline_collection_with_single_parameter_element_Count(bool async)
    {
        await base.Inline_collection_with_single_parameter_element_Count(async);

        AssertSql(
            """
@__i_0='2'
@__i_0='2'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT IIF(@__i_0 IS NULL, NULL, CLNG(@__i_0)) AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_0`) AS `v`
    WHERE `v`.`Value` > `p`.`Id`) = 1
""");
    }

    public override async Task Inline_collection_Contains_with_EF_Parameter(bool async)
    {
        await base.Inline_collection_Contains_with_EF_Parameter(async);

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

    public override async Task Inline_collection_Count_with_column_predicate_with_EF_Parameter(bool async)
    {
        await base.Inline_collection_Count_with_column_predicate_with_EF_Parameter(async);

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

    public override Task Parameter_collection_Count(bool async)
        => AssertTranslationFailedWithDetails(() => base.Parameter_collection_Count(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override async Task Parameter_collection_of_ints_Contains_int(bool async)
    {
        await base.Parameter_collection_of_ints_Contains_int(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (10, 999)
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` NOT IN (10, 999)
""");
    }

    public override async Task Parameter_collection_HashSet_of_ints_Contains_int(bool async)
    {
        await base.Parameter_collection_HashSet_of_ints_Contains_int(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (10, 999)
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` NOT IN (10, 999)
""");
    }

    public override async Task Parameter_collection_of_ints_Contains_nullable_int(bool async)
    {
        await base.Parameter_collection_of_ints_Contains_nullable_int(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableInt` IN (10, 999)
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableInt` NOT IN (10, 999) OR `p`.`NullableInt` IS NULL
""");
    }

    public override async Task Parameter_collection_of_nullable_ints_Contains_int(bool async)
    {
        await base.Parameter_collection_of_nullable_ints_Contains_int(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` IN (10, 999)
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Int` NOT IN (10, 999)
""");
    }

    public override async Task Parameter_collection_of_nullable_ints_Contains_nullable_int(bool async)
    {
        await base.Parameter_collection_of_nullable_ints_Contains_nullable_int(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableInt` IS NULL OR `p`.`NullableInt` = 999
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableInt` IS NOT NULL AND `p`.`NullableInt` <> 999
""");
    }

    public override async Task Parameter_collection_of_strings_Contains_string(bool async)
    {
        await base.Parameter_collection_of_strings_Contains_string(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`String` IN ('10', '999')
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`String` NOT IN ('10', '999')
""");
    }

    public override async Task Parameter_collection_of_strings_Contains_nullable_string(bool async)
    {
        await base.Parameter_collection_of_strings_Contains_nullable_string(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableString` IN ('10', '999')
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableString` NOT IN ('10', '999') OR `p`.`NullableString` IS NULL
""");
    }

    public override async Task Parameter_collection_of_nullable_strings_Contains_string(bool async)
    {
        await base.Parameter_collection_of_nullable_strings_Contains_string(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`String` = '10'
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`String` <> '10'
""");
    }

    public override async Task Parameter_collection_of_nullable_strings_Contains_nullable_string(bool async)
    {
        await base.Parameter_collection_of_nullable_strings_Contains_nullable_string(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableString` IS NULL OR `p`.`NullableString` = '999'
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableString` IS NOT NULL AND `p`.`NullableString` <> '999'
""");
    }

    public override async Task Parameter_collection_of_DateTimes_Contains(bool async)
    {
        await base.Parameter_collection_of_DateTimes_Contains(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`DateTime` IN (#2020-01-10 12:30:00#, #9999-01-01#)
""");
    }

    public override async Task Parameter_collection_of_bools_Contains(bool async)
    {
        await base.Parameter_collection_of_bools_Contains(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Bool` = TRUE
""");
    }

    public override async Task Parameter_collection_of_enums_Contains(bool async)
    {
        await base.Parameter_collection_of_enums_Contains(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Enum` IN (0, 3)
""");
    }

    public override async Task Parameter_collection_null_Contains(bool async)
    {
        await base.Parameter_collection_null_Contains(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE 0 = 1
""");
    }

    public override async Task Parameter_collection_Contains_with_EF_Constant(bool async)
    {
        await base.Parameter_collection_Contains_with_EF_Constant(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` IN (2, 999, 1000)
""");
    }

    public override async Task Parameter_collection_Where_with_EF_Constant_Where_Any(bool async)
    {
        await base.Parameter_collection_Where_with_EF_Constant_Where_Any(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE EXISTS (
    SELECT 1
    FROM (SELECT 2 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `i_0`
    UNION
    SELECT 999 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `i_1`
    UNION
    SELECT 1000 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `i_2`) AS `i`
    WHERE `i`.`Value` > 0)
""");
    }

    public override async Task Parameter_collection_Count_with_column_predicate_with_EF_Constant(bool async)
    {
        await base.Parameter_collection_Count_with_column_predicate_with_EF_Constant(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT 2 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `i_0`
    UNION
    SELECT 999 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `i_1`
    UNION
    SELECT 1000 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `i_2`) AS `i`
    WHERE `i`.`Value` > `p`.`Id`) = 2
""");
    }

    public override Task Column_collection_of_ints_Contains(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_of_ints_Contains(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_of_nullable_ints_Contains(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_of_nullable_ints_Contains(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_of_nullable_ints_Contains_null(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_of_nullable_ints_Contains_null(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());


    public override Task Column_collection_of_strings_contains_null(bool async)
        => AssertTranslationFailed(() => base.Column_collection_of_strings_contains_null(async));

    public override Task Column_collection_of_nullable_strings_contains_null(bool async)
        => AssertTranslationFailed(() => base.Column_collection_of_strings_contains_null(async));

    public override Task Column_collection_of_bools_Contains(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_of_bools_Contains(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    [ConditionalFact]
    public virtual async Task Json_representation_of_bool_array()
    {
        await using var context = CreateContext();

        Assert.Equal(
            "[true,false]",
            await context.Database.SqlQuery<string>($"SELECT [Bools] AS [Value] FROM [PrimitiveCollectionsEntity] WHERE [Id] = 1").SingleAsync());
    }

    public override Task Column_collection_Count_method(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Count_method(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Length(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Length(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());
    public override Task Column_collection_Count_with_predicate(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Count_with_predicate(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Where_Count(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Where_Count(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_index_int(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_index_int(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_index_string(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_index_string(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_index_datetime(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_index_datetime(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_index_beyond_end(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_index_beyond_end(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Nullable_reference_column_collection_index_equals_nullable_column(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_index_beyond_end(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Non_nullable_reference_column_collection_index_equals_nullable_column(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_index_beyond_end(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override async Task Inline_collection_index_Column(bool async)
    {
        await base.Inline_collection_index_Column(async);

        AssertSql(
"""
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT [v].[Value]
    FROM (VALUES (0, CAST(1 AS int)), (1, 2), (2, 3)) AS [v]([_ord], [Value])
    ORDER BY [v].[_ord]
    OFFSET [p].[Int] ROWS FETCH NEXT 1 ROWS ONLY) = 1
""");
    }

    public override async Task Inline_collection_value_index_Column(bool async)
    {
        await base.Inline_collection_value_index_Column(async);

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT [v].[Value]
    FROM (VALUES (0, CAST(1 AS int)), (1, [p].[Int]), (2, 3)) AS [v]([_ord], [Value])
    ORDER BY [v].[_ord]
    OFFSET [p].[Int] ROWS FETCH NEXT 1 ROWS ONLY) = 1
""");
    }

    public override async Task Inline_collection_List_value_index_Column(bool async)
    {
        await base.Inline_collection_List_value_index_Column(async);

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT [v].[Value]
    FROM (VALUES (0, CAST(1 AS int)), (1, [p].[Int]), (2, 3)) AS [v]([_ord], [Value])
    ORDER BY [v].[_ord]
    OFFSET [p].[Int] ROWS FETCH NEXT 1 ROWS ONLY) = 1
""");
    }

    public override Task Parameter_collection_index_Column_equal_Column(bool async)
        => AssertTranslationFailedWithDetails(() => base.Parameter_collection_index_Column_equal_Column(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Parameter_collection_index_Column_equal_constant(bool async)
        => AssertTranslationFailedWithDetails(() => base.Parameter_collection_index_Column_equal_constant(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_ElementAt(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_ElementAt(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_First(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_First(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_FirstOrDefault(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_FirstOrDefault(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Single(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Single(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_SingleOrDefault(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_SingleOrDefault(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Skip(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Skip(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Take(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Take(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Skip_Take(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Skip_Take(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Where_Skip(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Where_Skip(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Where_Take(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Where_Take(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Where_Skip_Take(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Where_Skip_Take(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Contains_over_subquery(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Skip_Take(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_OrderByDescending_ElementAt(bool async)
        => AssertTranslationFailed(() => base.Column_collection_OrderByDescending_ElementAt(async));

    public override Task Column_collection_Where_ElementAt(bool async)
        => AssertTranslationFailed(() => base.Column_collection_Where_ElementAt(async));

    public override Task Column_collection_Any(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Any(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Distinct(bool async)
        => AssertTranslationFailed(() => base.Column_collection_Distinct(async));

    public override Task Column_collection_SelectMany(bool async)
        => AssertTranslationFailed(() => base.Column_collection_SelectMany(async));

    public override Task Column_collection_SelectMany_with_filter(bool async)
        => AssertTranslationFailed(() => base.Column_collection_SelectMany_with_filter(async));

    public override Task Column_collection_SelectMany_with_Select_to_anonymous_type(bool async)
        => AssertTranslationFailed(() => base.Column_collection_SelectMany_with_Select_to_anonymous_type(async));

    public override async Task Column_collection_projection_from_top_level(bool async)
    {
        await base.Column_collection_projection_from_top_level(async);

        AssertSql(
            """
SELECT `p`.`Ints`
FROM `PrimitiveCollectionsEntity` AS `p`
ORDER BY `p`.`Id`
""");
    }

    public override Task Column_collection_Join_parameter_collection(bool async)
        => AssertTranslationFailed(() => base.Column_collection_Join_parameter_collection(async));

    public override Task Inline_collection_Join_ordered_column_collection(bool async)
        => AssertTranslationFailedWithDetails(() => base.Inline_collection_Join_ordered_column_collection(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Parameter_collection_Concat_column_collection(bool async)
        => AssertTranslationFailedWithDetails(() => base.Parameter_collection_Concat_column_collection(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Parameter_collection_with_type_inference_for_JsonScalarExpression(bool async)
        => AssertTranslationFailed(() => base.Parameter_collection_Concat_column_collection(async));

    public override Task Column_collection_Union_parameter_collection(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Union_parameter_collection(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Intersect_inline_collection(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_Intersect_inline_collection(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Inline_collection_Except_column_collection(bool async)
        => AssertTranslationFailedWithDetails(() => base.Inline_collection_Except_column_collection(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_Where_Union(bool async)
        => AssertTranslationFailedWithDetails(() => base.Inline_collection_Except_column_collection(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override async Task Column_collection_equality_parameter_collection(bool async)
    {
        await base.Column_collection_equality_parameter_collection(async);

        AssertSql(
            $"""
@__ints_0='[1,10]' (Size = 255)

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Ints` = {AssertSqlHelper.Parameter("@__ints_0")}
""");
    }

    public override async Task Column_collection_Concat_parameter_collection_equality_inline_collection(bool async)
    {
        await base.Column_collection_Concat_parameter_collection_equality_inline_collection(async);

        AssertSql();
    }

    public override async Task Column_collection_equality_inline_collection(bool async)
    {
        await base.Column_collection_equality_inline_collection(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Ints` = '[1,10]'
""");
    }

    public override async Task Column_collection_equality_inline_collection_with_parameters(bool async)
    {
        await base.Column_collection_equality_inline_collection_with_parameters(async);

        AssertSql();
    }

    public override async Task Column_collection_Where_equality_inline_collection(bool async)
    {
        await base.Column_collection_Where_equality_inline_collection(async);

        AssertSql();
    }

    public override Task Parameter_collection_in_subquery_Union_column_collection_as_compiled_query(bool async)
        => AssertTranslationFailedWithDetails(() => base.Parameter_collection_in_subquery_Union_column_collection_as_compiled_query(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Parameter_collection_in_subquery_Union_column_collection(bool async)
        => AssertTranslationFailedWithDetails(() => base.Parameter_collection_in_subquery_Union_column_collection(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Parameter_collection_in_subquery_Union_column_collection_nested(bool async)
        => AssertTranslationFailedWithDetails(() => base.Parameter_collection_in_subquery_Union_column_collection_nested(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override void Parameter_collection_in_subquery_and_Convert_as_compiled_query()
    {
        // Base implementation asserts that a different exception is thrown
    }

    public override Task Parameter_collection_in_subquery_Count_as_compiled_query(bool async)
        => AssertTranslationFailed(() => base.Parameter_collection_in_subquery_Count_as_compiled_query(async));

    public override Task Column_collection_in_subquery_Union_parameter_collection(bool async)
        => AssertTranslationFailedWithDetails(() => base.Column_collection_in_subquery_Union_parameter_collection(async), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    // Base implementation asserts that a different exception is thrown
    public override Task Parameter_collection_in_subquery_Union_another_parameter_collection_as_compiled_query(bool async)
        => Assert.ThrowsAsync<EqualException>(
            () => base.Parameter_collection_in_subquery_Union_another_parameter_collection_as_compiled_query(async));

    public override async Task Project_collection_of_ints_simple(bool async)
    {
        await base.Project_collection_of_ints_simple(async);

        AssertSql(
            """
SELECT `p`.`Ints`
FROM `PrimitiveCollectionsEntity` AS `p`
ORDER BY `p`.`Id`
""");
    }

    public override Task Project_collection_of_ints_ordered(bool async)
        // we don't propagate error details from projection
        => AssertTranslationFailed(() => base.Project_collection_of_ints_ordered(async));

    public override Task Project_collection_of_datetimes_filtered(bool async)
        // we don't propagate error details from projection
        => AssertTranslationFailed(() => base.Project_collection_of_datetimes_filtered(async));

    public override async Task Project_collection_of_nullable_ints_with_paging(bool async)
    {
        await base.Project_collection_of_nullable_ints_with_paging(async);

        // client eval
        AssertSql(
            """
SELECT `p`.`NullableInts`
FROM `PrimitiveCollectionsEntity` AS `p`
ORDER BY `p`.`Id`
""");
    }

    public override Task Project_collection_of_nullable_ints_with_paging2(bool async)
        // we don't propagate error details from projection
        => AssertTranslationFailed(() => base.Project_collection_of_nullable_ints_with_paging2(async));

    public override async Task Project_collection_of_nullable_ints_with_paging3(bool async)
    {
        await base.Project_collection_of_nullable_ints_with_paging3(async);

        // client eval
        AssertSql(
            """
SELECT `p`.`NullableInts`
FROM `PrimitiveCollectionsEntity` AS `p`
ORDER BY `p`.`Id`
""");
    }

    public override async Task Project_collection_of_ints_with_distinct(bool async)
    {
        await base.Project_collection_of_ints_with_distinct(async);

        // client eval
        AssertSql(
            """
SELECT `p`.`Ints`
FROM `PrimitiveCollectionsEntity` AS `p`
ORDER BY `p`.`Id`
""");
    }

    public override async Task Project_collection_of_nullable_ints_with_distinct(bool async)
    {
        await base.Project_collection_of_nullable_ints_with_distinct(async);

        AssertSql("");
    }

    public override async Task Project_collection_of_ints_with_ToList_and_FirstOrDefault(bool async)
    {
        await base.Project_collection_of_ints_with_ToList_and_FirstOrDefault(async);

        AssertSql(
            """
SELECT TOP 1 `p`.`Ints`
FROM `PrimitiveCollectionsEntity` AS `p`
ORDER BY `p`.`Id`
""");
    }

    public override Task Project_multiple_collections(bool async)
        // we don't propagate error details from projection
        => AssertTranslationFailed(() => base.Project_multiple_collections(async));

    public override Task Project_empty_collection_of_nullables_and_collection_only_containing_nulls(bool async)
        // we don't propagate error details from projection
        => AssertTranslationFailed(() => base.Project_empty_collection_of_nullables_and_collection_only_containing_nulls(async));

    public override async Task Project_primitive_collections_element(bool async)
    {
        await base.Project_primitive_collections_element(async);

        AssertSql(
            """
SELECT `p`.`Ints`, `p`.`DateTimes`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`Id` < 4
ORDER BY `p`.`Id`
""");
    }

    public override async Task Project_inline_collection(bool async)
    {
        await base.Project_inline_collection(async);

        AssertSql(
            """
SELECT `p`.`String`
FROM `PrimitiveCollectionsEntity` AS `p`
""");
    }

    public override async Task Project_inline_collection_with_Union(bool async)
    {
        await base.Project_inline_collection_with_Union(async);

        AssertSql(
            """
SELECT [p].[Id], [u].[Value]
FROM [PrimitiveCollectionsEntity] AS [p]
OUTER APPLY (
    SELECT [v].[Value]
    FROM (VALUES ([p].[String])) AS [v]([Value])
    UNION
    SELECT [p0].[String] AS [Value]
    FROM [PrimitiveCollectionsEntity] AS [p0]
) AS [u]
ORDER BY [p].[Id]
""");
    }

    public override async Task Project_inline_collection_with_Concat(bool async)
    {
        await base.Project_inline_collection_with_Concat(async);

        AssertSql();
    }

    public override async Task Nested_contains_with_Lists_and_no_inferred_type_mapping(bool async)
    {
        await base.Nested_contains_with_Lists_and_no_inferred_type_mapping(async);

        AssertSql(
"""
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE IIF(`p`.`Int` IN (1, 2, 3), 'one', 'two') IN ('one', 'two', 'three')
""");
    }

    public override async Task Nested_contains_with_arrays_and_no_inferred_type_mapping(bool async)
    {
        await base.Nested_contains_with_arrays_and_no_inferred_type_mapping(async);

        AssertSql(
"""
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`String`, `p`.`Strings`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE IIF(`p`.`Int` IN (1, 2, 3), 'one', 'two') IN ('one', 'two', 'three')
""");
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

    private PrimitiveCollectionsContext CreateContext()
        => Fixture.CreateContext();

    public class PrimitiveCollectionsQueryJetFixture : PrimitiveCollectionsQueryFixtureBase, ITestSqlLoggerFactory
    {
        protected override string StoreName
            => "PrimitiveCollectionsTest";

        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override ITestStoreFactory TestStoreFactory
            => JetTestStoreFactory.Instance;
    }
}
