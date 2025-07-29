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
    FROM (SELECT CLNG(2) AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_0`) AS `v`
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
    FROM (SELECT CLNG(2) AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_0`
    UNION
    SELECT 999 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_1`) AS `v`
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
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MIN([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[Int])) AS [v]([Value])) = 30
""");
    }

    public override async Task Inline_collection_List_Min_with_two_values()
    {
        await base.Inline_collection_List_Min_with_two_values();

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MIN([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[Int])) AS [v]([Value])) = 30
""");
    }

    public override async Task Inline_collection_Max_with_two_values()
    {
        await base.Inline_collection_Max_with_two_values();

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MAX([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[Int])) AS [v]([Value])) = 30
""");
    }

    public override async Task Inline_collection_List_Max_with_two_values()
    {
        await base.Inline_collection_List_Max_with_two_values();

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MAX([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[Int])) AS [v]([Value])) = 30
""");
    }

    public override async Task Inline_collection_Min_with_three_values()
    {
        await base.Inline_collection_Min_with_three_values();

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

    public override async Task Inline_collection_List_Min_with_three_values()
    {
        await base.Inline_collection_List_Min_with_three_values();

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

    public override async Task Inline_collection_Max_with_three_values()
    {
        await base.Inline_collection_Max_with_three_values();

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

    public override async Task Inline_collection_List_Max_with_three_values()
    {
        await base.Inline_collection_List_Max_with_three_values();

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

    public override async Task Inline_collection_of_nullable_value_type_Min()
    {
        await base.Inline_collection_of_nullable_value_type_Min();

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

    public override async Task Inline_collection_of_nullable_value_type_Max()
    {
        await base.Inline_collection_of_nullable_value_type_Max();

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

    public override async Task Inline_collection_of_nullable_value_type_with_null_Min()
    {
        await base.Inline_collection_of_nullable_value_type_with_null_Min();

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MIN([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[NullableInt]), (NULL)) AS [v]([Value])) = 30
""");
    }

    public override async Task Inline_collection_of_nullable_value_type_with_null_Max()
    {
        await base.Inline_collection_of_nullable_value_type_with_null_Max();

        AssertSql(
            """
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT MAX([v].[Value])
    FROM (VALUES (CAST(30 AS int)), ([p].[NullableInt]), (NULL)) AS [v]([Value])) = 30
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
@i='2'

SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT IIF(@i IS NULL, NULL, CLNG(@i)) AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_0`) AS `v`
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

    public override Task Parameter_collection_Count()
        => AssertTranslationFailedWithDetails(() => base.Parameter_collection_Count(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

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
WHERE `p`.`DateTime` IN (CDATE(@dateTimes1), CDATE(@dateTimes2))
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
    FROM (SELECT CLNG(2) AS `Value`
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

    public override async Task Parameter_collection_Count_with_column_predicate_with_EF_Constant()
    {
        await base.Parameter_collection_Count_with_column_predicate_with_EF_Constant();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT CLNG(2) AS `Value`
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

    public override Task Column_collection_of_ints_Contains()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_of_ints_Contains(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Column_collection_of_strings_contains_null()
        => AssertTranslationFailed(() => base.Column_collection_of_strings_contains_null());

    public override Task Column_collection_of_nullable_strings_contains_null()
        => AssertTranslationFailed(() => base.Column_collection_of_strings_contains_null());

    public override Task Column_collection_of_bools_Contains()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_of_bools_Contains(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    [ConditionalFact]
    public virtual async Task Json_representation_of_bool_array()
    {
        await using var context = CreateContext();

        Assert.Equal(
            "[true,false]",
            await context.Database.SqlQuery<string>($"SELECT [Bools] AS [Value] FROM [PrimitiveCollectionsEntity] WHERE [Id] = 1").SingleAsync());
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
SELECT [p].[Id], [p].[Bool], [p].[Bools], [p].[DateTime], [p].[DateTimes], [p].[Enum], [p].[Enums], [p].[Int], [p].[Ints], [p].[NullableInt], [p].[NullableInts], [p].[NullableString], [p].[NullableStrings], [p].[String], [p].[Strings]
FROM [PrimitiveCollectionsEntity] AS [p]
WHERE (
    SELECT [v].[Value]
    FROM (VALUES (0, CAST(1 AS int)), (1, 2), (2, 3)) AS [v]([_ord], [Value])
    ORDER BY [v].[_ord]
    OFFSET [p].[Int] ROWS FETCH NEXT 1 ROWS ONLY) = 1
""");
    }

    public override async Task Inline_collection_value_index_Column()
    {
        await base.Inline_collection_value_index_Column();

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

    public override async Task Inline_collection_List_value_index_Column()
    {
        await base.Inline_collection_List_value_index_Column();

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

    public override Task Parameter_collection_index_Column_equal_Column()
        => AssertTranslationFailedWithDetails(() => base.Parameter_collection_index_Column_equal_Column(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    public override Task Parameter_collection_index_Column_equal_constant()
        => AssertTranslationFailedWithDetails(() => base.Parameter_collection_index_Column_equal_constant(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

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

    public override Task Parameter_collection_in_subquery_Count_as_compiled_query()
        => AssertTranslationFailed(() => base.Parameter_collection_in_subquery_Count_as_compiled_query());

    public override Task Column_collection_in_subquery_Union_parameter_collection()
        => AssertTranslationFailedWithDetails(() => base.Column_collection_in_subquery_Union_parameter_collection(), JetStrings.QueryingIntoJsonCollectionsNotSupported());

    // Base implementation asserts that a different exception is thrown
    public override Task Parameter_collection_in_subquery_Union_another_parameter_collection_as_compiled_query()
        => Assert.ThrowsAsync<EqualException>(
            () => base.Parameter_collection_in_subquery_Union_another_parameter_collection_as_compiled_query());

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

    public override Task Project_multiple_collections()
        // we don't propagate error details from projection
        => AssertTranslationFailed(() => base.Project_multiple_collections());

    public override Task Project_empty_collection_of_nullables_and_collection_only_containing_nulls()
        // we don't propagate error details from projection
        => AssertTranslationFailed(() => base.Project_empty_collection_of_nullables_and_collection_only_containing_nulls());

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

    public override async Task Project_inline_collection_with_Concat()
    {
        await base.Project_inline_collection_with_Concat();

        AssertSql();
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
WHERE IIF(`p`.`Int` IN (@ints1, @ints2, @ints3), 'one', 'two') IN (@strings1, @strings2, @strings3)
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
WHERE IIF(`p`.`Int` IN (@ints1, @ints2, @ints3), 'one', 'two') IN (@strings1, @strings2, @strings3)
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
    FROM (SELECT CLNG(0) AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_0`
    UNION
    SELECT 1 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_1`
    UNION
    SELECT 2 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_2`
    UNION
    SELECT 3 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_3`) AS `v`
    WHERE `v`.`Value` = `p`.`Int`) > 0
""");
    }

    public override async Task Parameter_collection_of_structs_Contains_struct(bool async)
    {
        await base.Parameter_collection_of_structs_Contains_struct(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`WrappedId` IN (22, 33)
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`WrappedId` NOT IN (11, 44)
""");
    }

    public override async Task Parameter_collection_of_structs_Contains_nullable_struct(bool async)
    {
        await base.Parameter_collection_of_structs_Contains_nullable_struct(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedId` IN (22, 33)
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedId` NOT IN (11, 44) OR `p`.`NullableWrappedId` IS NULL
""");
    }

    public override async Task Parameter_collection_of_structs_Contains_nullable_struct_with_nullable_comparer(bool async)
    {
        await base.Parameter_collection_of_structs_Contains_nullable_struct_with_nullable_comparer(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedIdWithNullableComparer` IN (22, 33)
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedId` NOT IN (11, 44) OR `p`.`NullableWrappedId` IS NULL
""");
    }

    public override async Task Parameter_collection_of_nullable_structs_Contains_struct(bool async)
    {
        await base.Parameter_collection_of_nullable_structs_Contains_struct(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`WrappedId` = 22
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`WrappedId` NOT IN (11, 44)
""");
    }

    public override async Task Parameter_collection_of_nullable_structs_Contains_nullable_struct(bool async)
    {
        await base.Parameter_collection_of_nullable_structs_Contains_nullable_struct(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedId` IS NULL OR `p`.`NullableWrappedId` = 22
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedId` NOT IN (11, 44) OR `p`.`NullableWrappedId` IS NULL
""");
    }

    public override async Task Parameter_collection_of_nullable_structs_Contains_nullable_struct_with_nullable_comparer(bool async)
    {
        await base.Parameter_collection_of_nullable_structs_Contains_nullable_struct_with_nullable_comparer(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedIdWithNullableComparer` IS NULL OR `p`.`NullableWrappedIdWithNullableComparer` = 22
""",
            //
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE `p`.`NullableWrappedIdWithNullableComparer` NOT IN (11, 44) OR `p`.`NullableWrappedIdWithNullableComparer` IS NULL
""");
    }

    public override async Task Values_of_enum_casted_to_underlying_value(bool async)
    {
        await base.Values_of_enum_casted_to_underlying_value(async);

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Bool`, `p`.`Bools`, `p`.`DateTime`, `p`.`DateTimes`, `p`.`Enum`, `p`.`Enums`, `p`.`Int`, `p`.`Ints`, `p`.`NullableInt`, `p`.`NullableInts`, `p`.`NullableString`, `p`.`NullableStrings`, `p`.`NullableWrappedId`, `p`.`NullableWrappedIdWithNullableComparer`, `p`.`String`, `p`.`Strings`, `p`.`WrappedId`
FROM `PrimitiveCollectionsEntity` AS `p`
WHERE (
    SELECT COUNT(*)
    FROM (SELECT CLNG(0) AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_0`
    UNION
    SELECT 1 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_1`
    UNION
    SELECT 2 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_2`
    UNION
    SELECT 3 AS `Value`
    FROM (SELECT COUNT(*) FROM `#Dual`) AS `v_3`) AS `v`
    WHERE `v`.`Value` = `p`.`Int`) > 0
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
