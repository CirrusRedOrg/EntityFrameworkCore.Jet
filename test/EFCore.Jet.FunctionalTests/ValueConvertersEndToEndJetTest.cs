﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests;

public class ValueConvertersEndToEndJetTest
    : ValueConvertersEndToEndTestBase<ValueConvertersEndToEndJetTest.ValueConvertersEndToEndJetFixture>
{
    public ValueConvertersEndToEndJetTest(ValueConvertersEndToEndJetFixture fixture)
        : base(fixture)
    {
    }

    [ConditionalTheory]
    [InlineData(nameof(ConvertingEntity.BoolAsChar), "varchar(1)", false)]
    [InlineData(nameof(ConvertingEntity.BoolAsNullableChar), "varchar(1)", false)]
    [InlineData(nameof(ConvertingEntity.BoolAsString), "varchar(3)", false)]
    [InlineData(nameof(ConvertingEntity.BoolAsInt), "int", false)]
    [InlineData(nameof(ConvertingEntity.BoolAsNullableString), "varchar(3)", false)]
    [InlineData(nameof(ConvertingEntity.BoolAsNullableInt), "int", false)]
    [InlineData(nameof(ConvertingEntity.IntAsLong), "bigint", false)]
    [InlineData(nameof(ConvertingEntity.IntAsNullableLong), "bigint", false)]
    [InlineData(nameof(ConvertingEntity.BytesAsString), "varchar(max)", false)]
    [InlineData(nameof(ConvertingEntity.BytesAsNullableString), "varchar(max)", false)]
    [InlineData(nameof(ConvertingEntity.CharAsString), "varchar(1)", false)]
    [InlineData(nameof(ConvertingEntity.CharAsNullableString), "varchar(1)", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeOffsetToBinary), "bigint", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeOffsetToNullableBinary), "bigint", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeOffsetToString), "varchar(48)", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeOffsetToNullableString), "varchar(48)", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeToBinary), "bigint", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeToNullableBinary), "bigint", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeToString), "varchar(48)", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeToNullableString), "varchar(48)", false)]
    [InlineData(nameof(ConvertingEntity.EnumToString), "varchar(max)", false)]
    [InlineData(nameof(ConvertingEntity.EnumToNullableString), "varchar(max)", false)]
    [InlineData(nameof(ConvertingEntity.EnumToNumber), "bigint", false)]
    [InlineData(nameof(ConvertingEntity.EnumToNullableNumber), "bigint", false)]
    [InlineData(nameof(ConvertingEntity.GuidToString), "varchar(36)", false)]
    [InlineData(nameof(ConvertingEntity.GuidToNullableString), "varchar(36)", false)]
    [InlineData(nameof(ConvertingEntity.GuidToBytes), "varbinary(16)", false)]
    [InlineData(nameof(ConvertingEntity.GuidToNullableBytes), "varbinary(16)", false)]
    [InlineData(nameof(ConvertingEntity.IPAddressToString), "varchar(45)", false)]
    [InlineData(nameof(ConvertingEntity.IPAddressToNullableString), "varchar(45)", false)]
    [InlineData(nameof(ConvertingEntity.IPAddressToBytes), "varbinary(16)", false)]
    [InlineData(nameof(ConvertingEntity.IPAddressToNullableBytes), "varbinary(16)", false)]
    [InlineData(nameof(ConvertingEntity.PhysicalAddressToString), "varchar(20)", false)]
    [InlineData(nameof(ConvertingEntity.PhysicalAddressToNullableString), "varchar(20)", false)]
    [InlineData(nameof(ConvertingEntity.PhysicalAddressToBytes), "varbinary(8)", false)]
    [InlineData(nameof(ConvertingEntity.PhysicalAddressToNullableBytes), "varbinary(8)", false)]
    [InlineData(nameof(ConvertingEntity.NumberToString), "varchar(64)", false)]
    [InlineData(nameof(ConvertingEntity.NumberToNullableString), "varchar(64)", false)]
    [InlineData(nameof(ConvertingEntity.NumberToBytes), "varbinary(1)", false)]
    [InlineData(nameof(ConvertingEntity.NumberToNullableBytes), "varbinary(1)", false)]
    [InlineData(nameof(ConvertingEntity.StringToBool), "bit", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableBool), "bit", false)]
    [InlineData(nameof(ConvertingEntity.StringToBytes), "varbinary(max)", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableBytes), "varbinary(max)", false)]
    [InlineData(nameof(ConvertingEntity.StringToChar), "varchar(1)", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableChar), "varchar(1)", false)]
    [InlineData(nameof(ConvertingEntity.StringToDateTime), "datetime2", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableDateTime), "datetime2", false)]
    [InlineData(nameof(ConvertingEntity.StringToDateTimeOffset), "datetimeoffset", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableDateTimeOffset), "datetimeoffset", false)]
    [InlineData(nameof(ConvertingEntity.StringToEnum), "int", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableEnum), "int", false)]
    [InlineData(nameof(ConvertingEntity.StringToGuid), "uniqueidentifier", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableGuid), "uniqueidentifier", false)]
    [InlineData(nameof(ConvertingEntity.StringToNumber), "tinyint", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableNumber), "tinyint", false)]
    [InlineData(nameof(ConvertingEntity.StringToTimeSpan), "time", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableTimeSpan), "time", false)]
    [InlineData(nameof(ConvertingEntity.TimeSpanToTicks), "bigint", false)]
    [InlineData(nameof(ConvertingEntity.TimeSpanToNullableTicks), "bigint", false)]
    [InlineData(nameof(ConvertingEntity.TimeSpanToString), "varchar(48)", false)]
    [InlineData(nameof(ConvertingEntity.TimeSpanToNullableString), "varchar(48)", false)]
    [InlineData(nameof(ConvertingEntity.UriToString), "varchar(max)", false)]
    [InlineData(nameof(ConvertingEntity.UriToNullableString), "varchar(max)", false)]
    [InlineData(nameof(ConvertingEntity.NullableCharAsString), "varchar(1)", true)]
    [InlineData(nameof(ConvertingEntity.NullableCharAsNullableString), "varchar(1)", true)]
    [InlineData(nameof(ConvertingEntity.NullableBoolAsChar), "varchar(1)", true)]
    [InlineData(nameof(ConvertingEntity.NullableBoolAsNullableChar), "varchar(1)", true)]
    [InlineData(nameof(ConvertingEntity.NullableBoolAsString), "varchar(3)", true)]
    [InlineData(nameof(ConvertingEntity.NullableBoolAsNullableString), "varchar(3)", true)]
    [InlineData(nameof(ConvertingEntity.NullableBoolAsInt), "int", true)]
    [InlineData(nameof(ConvertingEntity.NullableBoolAsNullableInt), "int", true)]
    [InlineData(nameof(ConvertingEntity.NullableIntAsLong), "bigint", true)]
    [InlineData(nameof(ConvertingEntity.NullableIntAsNullableLong), "bigint", true)]
    [InlineData(nameof(ConvertingEntity.NullableBytesAsString), "varchar(max)", true)]
    [InlineData(nameof(ConvertingEntity.NullableBytesAsNullableString), "varchar(max)", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeOffsetToBinary), "bigint", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeOffsetToNullableBinary), "bigint", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeOffsetToString), "varchar(48)", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeOffsetToNullableString), "varchar(48)", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeToBinary), "bigint", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeToNullableBinary), "bigint", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeToString), "varchar(48)", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeToNullableString), "varchar(48)", true)]
    [InlineData(nameof(ConvertingEntity.NullableEnumToString), "varchar(max)", true)]
    [InlineData(nameof(ConvertingEntity.NullableEnumToNullableString), "varchar(max)", true)]
    [InlineData(nameof(ConvertingEntity.NullableEnumToNumber), "bigint", true)]
    [InlineData(nameof(ConvertingEntity.NullableEnumToNullableNumber), "bigint", true)]
    [InlineData(nameof(ConvertingEntity.NullableGuidToString), "varchar(36)", true)]
    [InlineData(nameof(ConvertingEntity.NullableGuidToNullableString), "varchar(36)", true)]
    [InlineData(nameof(ConvertingEntity.NullableGuidToBytes), "varbinary(16)", true)]
    [InlineData(nameof(ConvertingEntity.NullableGuidToNullableBytes), "varbinary(16)", true)]
    [InlineData(nameof(ConvertingEntity.NullableIPAddressToString), "varchar(45)", true)]
    [InlineData(nameof(ConvertingEntity.NullableIPAddressToNullableString), "varchar(45)", true)]
    [InlineData(nameof(ConvertingEntity.NullableIPAddressToBytes), "varbinary(16)", true)]
    [InlineData(nameof(ConvertingEntity.NullableIPAddressToNullableBytes), "varbinary(16)", true)]
    [InlineData(nameof(ConvertingEntity.NullablePhysicalAddressToString), "varchar(20)", true)]
    [InlineData(nameof(ConvertingEntity.NullablePhysicalAddressToNullableString), "varchar(20)", true)]
    [InlineData(nameof(ConvertingEntity.NullablePhysicalAddressToBytes), "varbinary(8)", true)]
    [InlineData(nameof(ConvertingEntity.NullablePhysicalAddressToNullableBytes), "varbinary(8)", true)]
    [InlineData(nameof(ConvertingEntity.NullableNumberToString), "varchar(64)", true)]
    [InlineData(nameof(ConvertingEntity.NullableNumberToNullableString), "varchar(64)", true)]
    [InlineData(nameof(ConvertingEntity.NullableNumberToBytes), "varbinary(1)", true)]
    [InlineData(nameof(ConvertingEntity.NullableNumberToNullableBytes), "varbinary(1)", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToBool), "bit", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableBool), "bit", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToBytes), "varbinary(max)", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableBytes), "varbinary(max)", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToChar), "varchar(1)", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableChar), "varchar(1)", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToDateTime), "datetime2", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableDateTime), "datetime2", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToDateTimeOffset), "datetimeoffset", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableDateTimeOffset), "datetimeoffset", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToEnum), "int", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableEnum), "int", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToGuid), "uniqueidentifier", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableGuid), "uniqueidentifier", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNumber), "tinyint", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableNumber), "tinyint", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToTimeSpan), "time", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableTimeSpan), "time", true)]
    [InlineData(nameof(ConvertingEntity.NullableTimeSpanToTicks), "bigint", true)]
    [InlineData(nameof(ConvertingEntity.NullableTimeSpanToNullableTicks), "bigint", true)]
    [InlineData(nameof(ConvertingEntity.NullableTimeSpanToString), "varchar(48)", true)]
    [InlineData(nameof(ConvertingEntity.NullableTimeSpanToNullableString), "varchar(48)", true)]
    [InlineData(nameof(ConvertingEntity.NullableUriToString), "varchar(max)", true)]
    [InlineData(nameof(ConvertingEntity.NullableUriToNullableString), "varchar(max)", true)]
    [InlineData(nameof(ConvertingEntity.NullStringToNonNullString), "varchar(max)", false)]
    [InlineData(nameof(ConvertingEntity.NonNullStringToNullString), "varchar(max)", true)]
    [InlineData(nameof(ConvertingEntity.NullableListOfInt), "varchar(max)", true)]
    [InlineData(nameof(ConvertingEntity.ListOfInt), "varchar(max)", false)]
    public virtual void Properties_with_conversions_map_to_appropriately_null_columns(
        string propertyName,
        string databaseType,
        bool isNullable)
    {
        using var context = CreateContext();

        var property = context.Model.FindEntityType(typeof(ConvertingEntity))!.FindProperty(propertyName);

        Assert.Equal(databaseType, property!.GetColumnType());
        Assert.Equal(isNullable, property!.IsNullable);
    }

    /*[ConditionalFact]
    public virtual void Can_use_custom_converters_without_property()
    {
        Fixture.TestSqlLoggerFactory.Clear();

        using (var context = CreateContext())
        {
            Assert.Empty(
                context.Set<ConvertingEntity>()
                    .Where(e => EF.Functions.DataLength((string)(object)new WrappedString { Value = "" }) == 1).ToList());
        }

        Assert.Equal(
"""
SELECT [c].[Id], [c].[BoolAsChar], [c].[BoolAsInt], [c].[BoolAsNullableChar], [c].[BoolAsNullableInt], [c].[BoolAsNullableString], [c].[BoolAsString], [c].[BytesAsNullableString], [c].[BytesAsString], [c].[CharAsNullableString], [c].[CharAsString], [c].[DateTimeOffsetToBinary], [c].[DateTimeOffsetToNullableBinary], [c].[DateTimeOffsetToNullableString], [c].[DateTimeOffsetToString], [c].[DateTimeToBinary], [c].[DateTimeToNullableBinary], [c].[DateTimeToNullableString], [c].[DateTimeToString], [c].[EnumToNullableNumber], [c].[EnumToNullableString], [c].[EnumToNumber], [c].[EnumToString], [c].[EnumerableOfInt], [c].[GuidToBytes], [c].[GuidToNullableBytes], [c].[GuidToNullableString], [c].[GuidToString], [c].[IPAddressToBytes], [c].[IPAddressToNullableBytes], [c].[IPAddressToNullableString], [c].[IPAddressToString], [c].[IntAsLong], [c].[IntAsNullableLong], [c].[ListOfInt], [c].[NonNullIntToNonNullString], [c].[NonNullIntToNullString], [c].[NonNullStringToNullString], [c].[NullIntToNonNullString], [c].[NullIntToNullString], [c].[NullStringToNonNullString], [c].[NullableBoolAsChar], [c].[NullableBoolAsInt], [c].[NullableBoolAsNullableChar], [c].[NullableBoolAsNullableInt], [c].[NullableBoolAsNullableString], [c].[NullableBoolAsString], [c].[NullableBytesAsNullableString], [c].[NullableBytesAsString], [c].[NullableCharAsNullableString], [c].[NullableCharAsString], [c].[NullableDateTimeOffsetToBinary], [c].[NullableDateTimeOffsetToNullableBinary], [c].[NullableDateTimeOffsetToNullableString], [c].[NullableDateTimeOffsetToString], [c].[NullableDateTimeToBinary], [c].[NullableDateTimeToNullableBinary], [c].[NullableDateTimeToNullableString], [c].[NullableDateTimeToString], [c].[NullableEnumToNullableNumber], [c].[NullableEnumToNullableString], [c].[NullableEnumToNumber], [c].[NullableEnumToString], [c].[NullableEnumerableOfInt], [c].[NullableGuidToBytes], [c].[NullableGuidToNullableBytes], [c].[NullableGuidToNullableString], [c].[NullableGuidToString], [c].[NullableIPAddressToBytes], [c].[NullableIPAddressToNullableBytes], [c].[NullableIPAddressToNullableString], [c].[NullableIPAddressToString], [c].[NullableIntAsLong], [c].[NullableIntAsNullableLong], [c].[NullableListOfInt], [c].[NullableNumberToBytes], [c].[NullableNumberToNullableBytes], [c].[NullableNumberToNullableString], [c].[NullableNumberToString], [c].[NullablePhysicalAddressToBytes], [c].[NullablePhysicalAddressToNullableBytes], [c].[NullablePhysicalAddressToNullableString], [c].[NullablePhysicalAddressToString], [c].[NullableStringToBool], [c].[NullableStringToBytes], [c].[NullableStringToChar], [c].[NullableStringToDateTime], [c].[NullableStringToDateTimeOffset], [c].[NullableStringToEnum], [c].[NullableStringToGuid], [c].[NullableStringToNullableBool], [c].[NullableStringToNullableBytes], [c].[NullableStringToNullableChar], [c].[NullableStringToNullableDateTime], [c].[NullableStringToNullableDateTimeOffset], [c].[NullableStringToNullableEnum], [c].[NullableStringToNullableGuid], [c].[NullableStringToNullableNumber], [c].[NullableStringToNullableTimeSpan], [c].[NullableStringToNumber], [c].[NullableStringToTimeSpan], [c].[NullableTimeSpanToNullableString], [c].[NullableTimeSpanToNullableTicks], [c].[NullableTimeSpanToString], [c].[NullableTimeSpanToTicks], [c].[NullableUriToNullableString], [c].[NullableUriToString], [c].[NumberToBytes], [c].[NumberToNullableBytes], [c].[NumberToNullableString], [c].[NumberToString], [c].[PhysicalAddressToBytes], [c].[PhysicalAddressToNullableBytes], [c].[PhysicalAddressToNullableString], [c].[PhysicalAddressToString], [c].[StringToBool], [c].[StringToBytes], [c].[StringToChar], [c].[StringToDateTime], [c].[StringToDateTimeOffset], [c].[StringToEnum], [c].[StringToGuid], [c].[StringToNullableBool], [c].[StringToNullableBytes], [c].[StringToNullableChar], [c].[StringToNullableDateTime], [c].[StringToNullableDateTimeOffset], [c].[StringToNullableEnum], [c].[StringToNullableGuid], [c].[StringToNullableNumber], [c].[StringToNullableTimeSpan], [c].[StringToNumber], [c].[StringToTimeSpan], [c].[TimeSpanToNullableString], [c].[TimeSpanToNullableTicks], [c].[TimeSpanToString], [c].[TimeSpanToTicks], [c].[UriToNullableString], [c].[UriToString]
FROM [ConvertingEntity] AS [c]
WHERE CAST(DATALENGTH(CAST(N'' AS nvarchar(max))) AS int) = 1
""",
            Fixture.TestSqlLoggerFactory.SqlStatements[0],
            ignoreLineEndingDifferences: true);
    }*/

    private struct WrappedString
    {
        public string Value { get; init; }
    }

    private class WrappedStringToStringConverter : ValueConverter<WrappedString, string>
    {
        public WrappedStringToStringConverter()
            : base(v => v.Value, v => new WrappedString { Value = v })
        {
        }
    }

    public class ValueConvertersEndToEndJetFixture : ValueConvertersEndToEndFixtureBase
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);

            configurationBuilder.DefaultTypeMapping<WrappedString>().HasConversion<WrappedStringToStringConverter>();
        }

        protected override ITestStoreFactory TestStoreFactory
            => JetTestStoreFactory.Instance;

        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);

            modelBuilder.Entity<ConvertingEntity>(
                b =>
                {
                    b.Property(e => e.NullableListOfInt).HasDefaultValue(new List<int>());
                    b.Property(e => e.ListOfInt).HasDefaultValue(new List<int>());
                    b.Property(e => e.NullableEnumerableOfInt).HasDefaultValue(Enumerable.Empty<int>());
                    b.Property(e => e.EnumerableOfInt).HasDefaultValue(Enumerable.Empty<int>());
                });
        }
    }
}

#nullable restore