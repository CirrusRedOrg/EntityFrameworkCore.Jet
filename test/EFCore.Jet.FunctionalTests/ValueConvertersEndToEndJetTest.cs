// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net;
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

    private static readonly DateTimeOffset _dateTimeOffset1 = new DateTimeOffset(1973, 9, 3, 12, 10, 0, new TimeSpan(7, 0, 0)).UtcDateTime;
    private static readonly DateTimeOffset _dateTimeOffset2 = new DateTimeOffset(1973, 9, 3, 12, 10, 0, new TimeSpan(8, 0, 0)).UtcDateTime;
    private static readonly DateTime _dateTime1 = new(1973, 9, 3, 12, 10, 0);
    private static readonly DateTime _dateTime2 = new(1973, 9, 3, 12, 10, 1);
    private static readonly DateOnly _dateOnly1 = new(1973, 9, 3);
    private static readonly DateOnly _dateOnly2 = new(1973, 9, 4);
    private static readonly IPAddress _ipAddress1 = IPAddress.Parse("127.0.0.1");
    private static readonly IPAddress _ipAddress2 = IPAddress.Parse("127.0.0.2");
    private static readonly PhysicalAddress _physicalAddress1 = PhysicalAddress.Parse("1D4E55D69273");
    private static readonly PhysicalAddress _physicalAddress2 = PhysicalAddress.Parse("1D4E55D69274");
    private static readonly TimeSpan _timeSpan1 = new(7, 0, 0);
    private static readonly TimeSpan _timeSpan2 = new(8, 0, 0);
    private static readonly Uri _uri1 = new("http://localhost/");
    private static readonly Uri _uri2 = new("http://microsoft.com/");
    private static readonly string _dateTimeFormat = @"yyyy\-MM\-dd HH\:mm\:ss.FFFFFFF";
    private static readonly string _dateOnlyFormat = @"yyyy\-MM\-dd";
    private static readonly string _dateTimeOffsetFormat = @"yyyy\-MM\-dd HH\:mm\:ss.FFFFFFFzzz";

    protected new static Dictionary<Type, object?[]> TestValues = new()
    {
        { typeof(bool), new object?[] { true, false, true, false } },
        { typeof(int), new object?[] { 77, 0, 78, 0 } },
        { typeof(char), new object?[] { 'A', 'B', 'C', 'D' } },
        { typeof(byte[]), new object?[] { new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 }, new byte[] { 4 } } },
        { typeof(DateTimeOffset), new object?[] { _dateTimeOffset1, _dateTimeOffset2, _dateTimeOffset1, _dateTimeOffset2 } },
        { typeof(DateTime), new object?[] { _dateTime1, _dateTime2, _dateTime1, _dateTime2 } },
        { typeof(DateOnly), new object?[] { _dateOnly1, _dateOnly2, _dateOnly1, _dateOnly2 } },
        { typeof(TheExperience), new object?[] { TheExperience.Jimi, TheExperience.Mitch, TheExperience.Noel, TheExperience.Jimi } },
        { typeof(Guid), new object?[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() } },
        { typeof(IPAddress), new object?[] { _ipAddress1, _ipAddress2, _ipAddress1, _ipAddress2 } },
        { typeof(ulong), new object?[] { (ulong)77, (ulong)0, (ulong)78, (ulong)0 } },
        { typeof(sbyte), new object?[] { (sbyte)-77, (sbyte)0, (sbyte)78, (sbyte)0 } },
        { typeof(PhysicalAddress), new object?[] { _physicalAddress1, _physicalAddress2, _physicalAddress1, _physicalAddress2 } },
        { typeof(TimeSpan), new object?[] { _timeSpan1, _timeSpan2, _timeSpan1, _timeSpan2 } },
        { typeof(Uri), new object?[] { _uri1, _uri2, _uri1, _uri2 } },
        {
            typeof(List<int>), new object?[]
            {
                new List<int>
                {
                    47,
                    48,
                    47,
                    46
                },
                new List<int>
                {
                    57,
                    58,
                    57,
                    56
                },
                new List<int>
                {
                    67,
                    68,
                    67,
                    66
                },
                new List<int>
                {
                    77,
                    78,
                    77,
                    76
                },
            }
        },
        {
            typeof(IEnumerable<int>), new object?[]
            {
                new List<int>
                {
                    47,
                    48,
                    47,
                    46
                },
                new List<int>
                {
                    57,
                    58,
                    57,
                    56
                },
                new List<int>
                {
                    67,
                    68,
                    67,
                    66
                },
                new List<int>
                {
                    77,
                    78,
                    77,
                    76
                },
            }
        },
    };

    protected new static Dictionary<Type, object?[]> StringTestValues = new()
    {
        { typeof(bool), new object?[] { "True", "False", "True", "False" } },
        { typeof(char), new object?[] { "A", "B", "C", "D" } },
        { typeof(byte[]), new object?[] { "", "", "", "" } },
        {
            typeof(DateTimeOffset),
            new object?[]
            {
                _dateTimeOffset1.ToString(_dateTimeOffsetFormat),
                _dateTimeOffset2.ToString(_dateTimeOffsetFormat),
                _dateTimeOffset1.ToString(_dateTimeOffsetFormat),
                _dateTimeOffset2.ToString(_dateTimeOffsetFormat)
            }
        },
        {
            typeof(DateTime),
            new object?[]
            {
                _dateTime1.ToString(_dateTimeFormat),
                _dateTime2.ToString(_dateTimeFormat),
                _dateTime1.ToString(_dateTimeFormat),
                _dateTime2.ToString(_dateTimeFormat)
            }
        },
        {
            typeof(DateOnly),
            new object?[]
            {
                _dateOnly1.ToString(_dateOnlyFormat),
                _dateOnly2.ToString(_dateOnlyFormat),
                _dateOnly1.ToString(_dateOnlyFormat),
                _dateOnly2.ToString(_dateOnlyFormat)
            }
        },
        { typeof(string), new object?[] { "A", "<null>", "C", "<null>" } },
        {
            typeof(TheExperience),
            new object?[]
            {
                nameof(TheExperience.Jimi), nameof(TheExperience.Mitch), nameof(TheExperience.Noel), nameof(TheExperience.Jimi)
            }
        },
        {
            typeof(Guid),
            new object?[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString() }
        },
        { typeof(ulong), new object?[] { "77", "0", "78", "0" } },
        { typeof(sbyte), new object?[] { "-77", "75", "-78", "0" } },
        { typeof(byte), new object?[] { "77", "75", "78", "0" } },
        { typeof(TimeSpan), new object?[] { _timeSpan1.ToString(), _timeSpan2.ToString(), _timeSpan1.ToString(), _timeSpan2.ToString() } },
    };

    [ConditionalTheory]
    [InlineData(nameof(ConvertingEntity.BoolAsChar), "varchar(1)", false)]
    [InlineData(nameof(ConvertingEntity.BoolAsNullableChar), "varchar(1)", false)]
    [InlineData(nameof(ConvertingEntity.BoolAsString), "varchar(3)", false)]
    [InlineData(nameof(ConvertingEntity.BoolAsInt), "integer", false)]
    [InlineData(nameof(ConvertingEntity.BoolAsNullableString), "varchar(3)", false)]
    [InlineData(nameof(ConvertingEntity.BoolAsNullableInt), "integer", false)]
    [InlineData(nameof(ConvertingEntity.IntAsLong), "decimal(20,0)", false)]
    [InlineData(nameof(ConvertingEntity.IntAsNullableLong), "decimal(20,0)", false)]
    [InlineData(nameof(ConvertingEntity.BytesAsString), "varchar(255)", false)]
    [InlineData(nameof(ConvertingEntity.BytesAsNullableString), "varchar(255)", false)]
    [InlineData(nameof(ConvertingEntity.CharAsString), "varchar(1)", false)]
    [InlineData(nameof(ConvertingEntity.CharAsNullableString), "varchar(1)", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeOffsetToBinary), "decimal(20,0)", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeOffsetToNullableBinary), "decimal(20,0)", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeOffsetToString), "varchar(48)", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeOffsetToNullableString), "varchar(48)", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeToBinary), "decimal(20,0)", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeToNullableBinary), "decimal(20,0)", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeToString), "varchar(48)", false)]
    [InlineData(nameof(ConvertingEntity.DateTimeToNullableString), "varchar(48)", false)]
    [InlineData(nameof(ConvertingEntity.EnumToString), "varchar(255)", false)]
    [InlineData(nameof(ConvertingEntity.EnumToNullableString), "varchar(255)", false)]
    [InlineData(nameof(ConvertingEntity.EnumToNumber), "decimal(20,0)", false)]
    [InlineData(nameof(ConvertingEntity.EnumToNullableNumber), "decimal(20,0)", false)]
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
    [InlineData(nameof(ConvertingEntity.StringToBool), "smallint", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableBool), "smallint", false)]
    [InlineData(nameof(ConvertingEntity.StringToBytes), "longbinary", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableBytes), "longbinary", false)]
    [InlineData(nameof(ConvertingEntity.StringToChar), "varchar(1)", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableChar), "varchar(1)", false)]
    [InlineData(nameof(ConvertingEntity.StringToDateTime), "datetime", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableDateTime), "datetime", false)]
    [InlineData(nameof(ConvertingEntity.StringToDateTimeOffset), "datetime", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableDateTimeOffset), "datetime", false)]
    [InlineData(nameof(ConvertingEntity.StringToEnum), "integer", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableEnum), "integer", false)]
    [InlineData(nameof(ConvertingEntity.StringToGuid), "uniqueidentifier", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableGuid), "uniqueidentifier", false)]
    [InlineData(nameof(ConvertingEntity.StringToNumber), "byte", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableNumber), "byte", false)]
    [InlineData(nameof(ConvertingEntity.StringToTimeSpan), "datetime", false)]
    [InlineData(nameof(ConvertingEntity.StringToNullableTimeSpan), "datetime", false)]
    [InlineData(nameof(ConvertingEntity.TimeSpanToTicks), "decimal(20,0)", false)]
    [InlineData(nameof(ConvertingEntity.TimeSpanToNullableTicks), "decimal(20,0)", false)]
    [InlineData(nameof(ConvertingEntity.TimeSpanToString), "varchar(48)", false)]
    [InlineData(nameof(ConvertingEntity.TimeSpanToNullableString), "varchar(48)", false)]
    [InlineData(nameof(ConvertingEntity.UriToString), "varchar(255)", false)]
    [InlineData(nameof(ConvertingEntity.UriToNullableString), "varchar(255)", false)]
    [InlineData(nameof(ConvertingEntity.NullableCharAsString), "varchar(1)", true)]
    [InlineData(nameof(ConvertingEntity.NullableCharAsNullableString), "varchar(1)", true)]
    [InlineData(nameof(ConvertingEntity.NullableBoolAsChar), "varchar(1)", true)]
    [InlineData(nameof(ConvertingEntity.NullableBoolAsNullableChar), "varchar(1)", true)]
    [InlineData(nameof(ConvertingEntity.NullableBoolAsString), "varchar(3)", true)]
    [InlineData(nameof(ConvertingEntity.NullableBoolAsNullableString), "varchar(3)", true)]
    [InlineData(nameof(ConvertingEntity.NullableBoolAsInt), "integer", true)]
    [InlineData(nameof(ConvertingEntity.NullableBoolAsNullableInt), "integer", true)]
    [InlineData(nameof(ConvertingEntity.NullableIntAsLong), "decimal(20,0)", true)]
    [InlineData(nameof(ConvertingEntity.NullableIntAsNullableLong), "decimal(20,0)", true)]
    [InlineData(nameof(ConvertingEntity.NullableBytesAsString), "varchar(255)", true)]
    [InlineData(nameof(ConvertingEntity.NullableBytesAsNullableString), "varchar(255)", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeOffsetToBinary), "decimal(20,0)", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeOffsetToNullableBinary), "decimal(20,0)", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeOffsetToString), "varchar(48)", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeOffsetToNullableString), "varchar(48)", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeToBinary), "decimal(20,0)", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeToNullableBinary), "decimal(20,0)", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeToString), "varchar(48)", true)]
    [InlineData(nameof(ConvertingEntity.NullableDateTimeToNullableString), "varchar(48)", true)]
    [InlineData(nameof(ConvertingEntity.NullableEnumToString), "varchar(255)", true)]
    [InlineData(nameof(ConvertingEntity.NullableEnumToNullableString), "varchar(255)", true)]
    [InlineData(nameof(ConvertingEntity.NullableEnumToNumber), "decimal(20,0)", true)]
    [InlineData(nameof(ConvertingEntity.NullableEnumToNullableNumber), "decimal(20,0)", true)]
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
    [InlineData(nameof(ConvertingEntity.NullableStringToBool), "smallint", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableBool), "smallint", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToBytes), "longbinary", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableBytes), "longbinary", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToChar), "varchar(1)", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableChar), "varchar(1)", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToDateTime), "datetime", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableDateTime), "datetime", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToDateTimeOffset), "datetime", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableDateTimeOffset), "datetime", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToEnum), "integer", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableEnum), "integer", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToGuid), "uniqueidentifier", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableGuid), "uniqueidentifier", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNumber), "byte", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableNumber), "byte", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToTimeSpan), "datetime", true)]
    [InlineData(nameof(ConvertingEntity.NullableStringToNullableTimeSpan), "datetime", true)]
    [InlineData(nameof(ConvertingEntity.NullableTimeSpanToTicks), "decimal(20,0)", true)]
    [InlineData(nameof(ConvertingEntity.NullableTimeSpanToNullableTicks), "decimal(20,0)", true)]
    [InlineData(nameof(ConvertingEntity.NullableTimeSpanToString), "varchar(48)", true)]
    [InlineData(nameof(ConvertingEntity.NullableTimeSpanToNullableString), "varchar(48)", true)]
    [InlineData(nameof(ConvertingEntity.NullableUriToString), "varchar(255)", true)]
    [InlineData(nameof(ConvertingEntity.NullableUriToNullableString), "varchar(255)", true)]
    [InlineData(nameof(ConvertingEntity.NullStringToNonNullString), "varchar(255)", false)]
    [InlineData(nameof(ConvertingEntity.NonNullStringToNullString), "varchar(255)", true)]
    [InlineData(nameof(ConvertingEntity.NullableListOfInt), "varchar(255)", true)]
    [InlineData(nameof(ConvertingEntity.ListOfInt), "varchar(255)", false)]
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
