// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class ConvertToProviderTypesJetTest : ConvertToProviderTypesTestBase<
        ConvertToProviderTypesJetTest.ConvertToProviderTypesJetFixture>
    {
        public ConvertToProviderTypesJetTest(ConvertToProviderTypesJetFixture fixture)
            : base(fixture)
        {
        }

        [ConditionalFact]
        public virtual void Columns_have_expected_data_types()
        {
            var actual = BuiltInDataTypesJetTest.QueryForColumnTypes(
                CreateContext(),
                nameof(ObjectBackedDataTypes), nameof(NullableBackedDataTypes), nameof(NonNullableBackedDataTypes));

            const string expected = @"#Dual.ID ---> [integer]
Animal.Id ---> [counter]
AnimalDetails.AnimalId ---> [nullable integer]
AnimalDetails.BoolField ---> [integer]
AnimalDetails.Id ---> [counter]
AnimalIdentification.AnimalId ---> [integer]
AnimalIdentification.Id ---> [counter]
AnimalIdentification.Method ---> [varchar] [MaxLength = 6]
BinaryForeignKeyDataType.BinaryKeyDataTypeId ---> [nullable varchar] [MaxLength = 255]
BinaryForeignKeyDataType.Id ---> [integer]
BinaryKeyDataType.Ex ---> [nullable varchar] [MaxLength = 255]
BinaryKeyDataType.Id ---> [varchar] [MaxLength = 255]
BuiltInDataTypes.Enum16 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.Enum32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.Enum64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.Enum8 ---> [char] [MaxLength = 17]
BuiltInDataTypes.EnumS8 ---> [varchar] [MaxLength = 255]
BuiltInDataTypes.EnumU16 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.EnumU32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.EnumU64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.Id ---> [integer]
BuiltInDataTypes.PartitionId ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestBoolean ---> [varchar] [MaxLength = 1]
BuiltInDataTypes.TestByte ---> [integer]
BuiltInDataTypes.TestCharacter ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestDateOnly ---> [datetime]
BuiltInDataTypes.TestDateTime ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestDateTimeOffset ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestDecimal ---> [varbinary] [MaxLength = 16]
BuiltInDataTypes.TestDouble ---> [decimal] [Precision = 28 Scale = 17]
BuiltInDataTypes.TestInt16 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestInt32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestInt64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestSignedByte ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestSingle ---> [decimal] [Precision = 28 Scale = 17]
BuiltInDataTypes.TestTimeOnly ---> [datetime]
BuiltInDataTypes.TestTimeSpan ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestUnsignedInt16 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestUnsignedInt32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestUnsignedInt64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.Enum16 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.Enum32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.Enum64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.Enum8 ---> [varchar] [MaxLength = 255]
BuiltInDataTypesShadow.EnumS8 ---> [varchar] [MaxLength = 255]
BuiltInDataTypesShadow.EnumU16 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.EnumU32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.EnumU64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.Id ---> [integer]
BuiltInDataTypesShadow.PartitionId ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestBoolean ---> [varchar] [MaxLength = 1]
BuiltInDataTypesShadow.TestByte ---> [integer]
BuiltInDataTypesShadow.TestCharacter ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestDateOnly ---> [datetime]
BuiltInDataTypesShadow.TestDateTime ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestDateTimeOffset ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestDecimal ---> [varbinary] [MaxLength = 16]
BuiltInDataTypesShadow.TestDouble ---> [decimal] [Precision = 28 Scale = 17]
BuiltInDataTypesShadow.TestInt16 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestInt32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestInt64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestSignedByte ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestSingle ---> [decimal] [Precision = 28 Scale = 17]
BuiltInDataTypesShadow.TestTimeOnly ---> [datetime]
BuiltInDataTypesShadow.TestTimeSpan ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestUnsignedInt16 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestUnsignedInt32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestUnsignedInt64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.Enum16 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.Enum32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.Enum64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.Enum8 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypes.EnumS8 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypes.EnumU16 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.EnumU32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.EnumU64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.Id ---> [integer]
BuiltInNullableDataTypes.PartitionId ---> [decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestByteArray ---> [nullable longbinary]
BuiltInNullableDataTypes.TestNullableBoolean ---> [nullable varchar] [MaxLength = 1]
BuiltInNullableDataTypes.TestNullableByte ---> [nullable integer]
BuiltInNullableDataTypes.TestNullableCharacter ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableDateOnly ---> [nullable datetime]
BuiltInNullableDataTypes.TestNullableDateTime ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableDateTimeOffset ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableDecimal ---> [nullable varbinary] [MaxLength = 16]
BuiltInNullableDataTypes.TestNullableDouble ---> [nullable decimal] [Precision = 28 Scale = 17]
BuiltInNullableDataTypes.TestNullableInt16 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableInt32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableInt64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableSignedByte ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableSingle ---> [nullable decimal] [Precision = 28 Scale = 17]
BuiltInNullableDataTypes.TestNullableTimeOnly ---> [nullable datetime]
BuiltInNullableDataTypes.TestNullableTimeSpan ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableUnsignedInt16 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableUnsignedInt32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableUnsignedInt64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestString ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.Enum16 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.Enum32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.Enum64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.Enum8 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.EnumS8 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.EnumU16 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.EnumU32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.EnumU64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.Id ---> [integer]
BuiltInNullableDataTypesShadow.PartitionId ---> [decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestByteArray ---> [nullable longbinary]
BuiltInNullableDataTypesShadow.TestNullableBoolean ---> [nullable varchar] [MaxLength = 1]
BuiltInNullableDataTypesShadow.TestNullableByte ---> [nullable integer]
BuiltInNullableDataTypesShadow.TestNullableCharacter ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestNullableDateOnly ---> [nullable datetime]
BuiltInNullableDataTypesShadow.TestNullableDateTime ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestNullableDateTimeOffset ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestNullableDecimal ---> [nullable varbinary] [MaxLength = 16]
BuiltInNullableDataTypesShadow.TestNullableDouble ---> [nullable decimal] [Precision = 28 Scale = 17]
BuiltInNullableDataTypesShadow.TestNullableInt16 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestNullableInt32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestNullableInt64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestNullableSignedByte ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestNullableSingle ---> [nullable decimal] [Precision = 28 Scale = 17]
BuiltInNullableDataTypesShadow.TestNullableTimeOnly ---> [nullable datetime]
BuiltInNullableDataTypesShadow.TestNullableTimeSpan ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestNullableUnsignedInt16 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestNullableUnsignedInt32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestNullableUnsignedInt64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestString ---> [nullable varchar] [MaxLength = 255]
DateTimeEnclosure.DateTimeOffset ---> [nullable datetime]
DateTimeEnclosure.Id ---> [counter]
EmailTemplate.Id ---> [guid]
EmailTemplate.TemplateType ---> [integer]
MaxLengthDataTypes.ByteArray5 ---> [nullable varchar] [MaxLength = 8]
MaxLengthDataTypes.ByteArray9000 ---> [nullable longchar]
MaxLengthDataTypes.Id ---> [integer]
MaxLengthDataTypes.String3 ---> [nullable varbinary] [MaxLength = 3]
MaxLengthDataTypes.String9000 ---> [nullable longbinary]
MaxLengthDataTypes.StringUnbounded ---> [nullable longbinary]
StringEnclosure.Id ---> [counter]
StringEnclosure.Value ---> [nullable varchar] [MaxLength = 255]
StringForeignKeyDataType.Id ---> [integer]
StringForeignKeyDataType.StringKeyDataTypeId ---> [nullable varbinary] [MaxLength = 510]
StringKeyDataType.Id ---> [varbinary] [MaxLength = 510]
UnicodeDataTypes.Id ---> [integer]
UnicodeDataTypes.StringAnsi ---> [nullable varchar] [MaxLength = 255]
UnicodeDataTypes.StringAnsi3 ---> [nullable varchar] [MaxLength = 3]
UnicodeDataTypes.StringAnsi9000 ---> [nullable longchar]
UnicodeDataTypes.StringDefault ---> [nullable varchar] [MaxLength = 255]
UnicodeDataTypes.StringUnicode ---> [nullable varchar] [MaxLength = 255]
";

            Assert.Equal(expected, actual, ignoreLineEndingDifferences: true);
        }

        public override void Can_insert_and_read_back_all_non_nullable_data_types()
        {
            using (var context = CreateContext())
            {
                context.Set<BuiltInDataTypes>().Add(
                    new BuiltInDataTypes
                    {
                        Id = 1,
                        PartitionId = 1,
                        TestInt16 = -1234,
                        TestInt32 = -123456789,
                        TestInt64 = -1234567890123456789L,
                        TestDouble = -1.23456789,
                        TestDecimal = -1234567890.01M,
                        TestDateTime = DateTime.Parse("01/01/2000 12:34:56", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal),
                        TestDateTimeOffset = new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0)),
                        TestTimeSpan = new TimeSpan(0, 10, 9, 8, 0),
                        TestDateOnly = new DateOnly(2020, 3, 1),
                        TestTimeOnly = new TimeOnly(12, 30, 45, 0),
                        TestSingle = -1.234F,
                        TestBoolean = true,
                        TestByte = 255,
                        TestUnsignedInt16 = 1234,
                        TestUnsignedInt32 = 1234565789U,
                        TestUnsignedInt64 = 1234567890123456789UL,
                        TestCharacter = 'a',
                        TestSignedByte = -128,
                        Enum64 = Enum64.SomeValue,
                        Enum32 = Enum32.SomeValue,
                        Enum16 = Enum16.SomeValue,
                        Enum8 = Enum8.SomeValue,
                        EnumU64 = EnumU64.SomeValue,
                        EnumU32 = EnumU32.SomeValue,
                        EnumU16 = EnumU16.SomeValue,
                        EnumS8 = EnumS8.SomeValue
                    });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                var dt = context.Set<BuiltInDataTypes>().Where(e => e.Id == 1).ToList().Single();

                var entityType = context.Model.FindEntityType(typeof(BuiltInDataTypes));
                AssertEqualIfMapped(entityType, (short)-1234, () => dt.TestInt16);
                AssertEqualIfMapped(entityType, -123456789, () => dt.TestInt32);
                AssertEqualIfMapped(entityType, -1234567890123456789L, () => dt.TestInt64);
                AssertEqualIfMapped(entityType, -1.23456789, () => dt.TestDouble);
                AssertEqualIfMapped(entityType, -1234567890.01M, () => dt.TestDecimal);
                AssertEqualIfMapped(
                    entityType, DateTime.Parse("01/01/2000 12:34:56", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal),
                    () => dt.TestDateTime);
                AssertEqualIfMapped(
                    entityType, new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0)),
                    () => dt.TestDateTimeOffset);
                AssertEqualIfMapped(entityType, new TimeSpan(0, 10, 9, 8, 0), () => dt.TestTimeSpan);
                AssertEqualIfMapped(entityType, new DateOnly(2020, 3, 1), () => dt.TestDateOnly);
                AssertEqualIfMapped(entityType, new TimeOnly(12, 30, 45, 0), () => dt.TestTimeOnly);
                AssertEqualIfMapped(entityType, -1.234F, () => dt.TestSingle);
                AssertEqualIfMapped(entityType, true, () => dt.TestBoolean);
                AssertEqualIfMapped(entityType, (byte)255, () => dt.TestByte);
                AssertEqualIfMapped(entityType, Enum64.SomeValue, () => dt.Enum64);
                AssertEqualIfMapped(entityType, Enum32.SomeValue, () => dt.Enum32);
                AssertEqualIfMapped(entityType, Enum16.SomeValue, () => dt.Enum16);
                AssertEqualIfMapped(entityType, Enum8.SomeValue, () => dt.Enum8);
                AssertEqualIfMapped(entityType, (ushort)1234, () => dt.TestUnsignedInt16);
                AssertEqualIfMapped(entityType, 1234565789U, () => dt.TestUnsignedInt32);
                AssertEqualIfMapped(entityType, 1234567890123456789UL, () => dt.TestUnsignedInt64);
                AssertEqualIfMapped(entityType, 'a', () => dt.TestCharacter);
                AssertEqualIfMapped(entityType, (sbyte)-128, () => dt.TestSignedByte);
                AssertEqualIfMapped(entityType, EnumU64.SomeValue, () => dt.EnumU64);
                AssertEqualIfMapped(entityType, EnumU32.SomeValue, () => dt.EnumU32);
                AssertEqualIfMapped(entityType, EnumU16.SomeValue, () => dt.EnumU16);
                AssertEqualIfMapped(entityType, EnumS8.SomeValue, () => dt.EnumS8);
            }
        }

        public override void Can_insert_and_read_back_all_nullable_data_types_with_values_set_to_non_null()
        {
            using (var context = CreateContext())
            {
                context.Set<BuiltInNullableDataTypes>().Add(
                    new BuiltInNullableDataTypes
                    {
                        Id = 101,
                        PartitionId = 101,
                        TestString = "TestString",
                        TestByteArray = new byte[] { 10, 9, 8, 7, 6 },
                        TestNullableInt16 = -1234,
                        TestNullableInt32 = -123456789,
                        TestNullableInt64 = -1234567890123456789L,
                        TestNullableDouble = -1.23456789,
                        TestNullableDecimal = -1234567890.01M,
                        TestNullableDateTime = DateTime.Parse("01/01/2000 12:34:56").ToUniversalTime(),
                        TestNullableDateTimeOffset = new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0)),
                        TestNullableDateOnly = new DateOnly(2020, 3, 1),
                        TestNullableTimeOnly = new TimeOnly(12, 30, 45, 0),
                        TestNullableTimeSpan = new TimeSpan(0, 10, 9, 8, 0),
                        TestNullableSingle = -1.234F,
                        TestNullableBoolean = false,
                        TestNullableByte = 255,
                        TestNullableUnsignedInt16 = 1234,
                        TestNullableUnsignedInt32 = 1234565789U,
                        TestNullableUnsignedInt64 = 1234567890123456789UL,
                        TestNullableCharacter = 'a',
                        TestNullableSignedByte = -128,
                        Enum64 = Enum64.SomeValue,
                        Enum32 = Enum32.SomeValue,
                        Enum16 = Enum16.SomeValue,
                        Enum8 = Enum8.SomeValue,
                        EnumU64 = EnumU64.SomeValue,
                        EnumU32 = EnumU32.SomeValue,
                        EnumU16 = EnumU16.SomeValue,
                        EnumS8 = EnumS8.SomeValue
                    });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                var dt = context.Set<BuiltInNullableDataTypes>().Where(ndt => ndt.Id == 101).ToList().Single();

                var entityType = context.Model.FindEntityType(typeof(BuiltInNullableDataTypes));
                AssertEqualIfMapped(entityType, "TestString", () => dt.TestString);
                AssertEqualIfMapped(entityType, new byte[] { 10, 9, 8, 7, 6 }, () => dt.TestByteArray);
                AssertEqualIfMapped(entityType, (short)-1234, () => dt.TestNullableInt16);
                AssertEqualIfMapped(entityType, -123456789, () => dt.TestNullableInt32);
                AssertEqualIfMapped(entityType, -1234567890123456789L, () => dt.TestNullableInt64);
                AssertEqualIfMapped(entityType, -1.23456789, () => dt.TestNullableDouble);
                AssertEqualIfMapped(entityType, -1234567890.01M, () => dt.TestNullableDecimal);
                AssertEqualIfMapped(entityType, DateTime.Parse("01/01/2000 12:34:56").ToUniversalTime(), () => dt.TestNullableDateTime);
                AssertEqualIfMapped(
                    entityType, new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0)),
                    () => dt.TestNullableDateTimeOffset);
                AssertEqualIfMapped(entityType, new TimeSpan(0, 10, 9, 8, 0), () => dt.TestNullableTimeSpan);
                AssertEqualIfMapped(entityType, new DateOnly(2020, 3, 1), () => dt.TestNullableDateOnly);
                AssertEqualIfMapped(entityType, new TimeOnly(12, 30, 45, 0), () => dt.TestNullableTimeOnly);
                AssertEqualIfMapped(entityType, -1.234F, () => dt.TestNullableSingle);
                AssertEqualIfMapped(entityType, false, () => dt.TestNullableBoolean);
                AssertEqualIfMapped(entityType, (byte)255, () => dt.TestNullableByte);
                AssertEqualIfMapped(entityType, Enum64.SomeValue, () => dt.Enum64);
                AssertEqualIfMapped(entityType, Enum32.SomeValue, () => dt.Enum32);
                AssertEqualIfMapped(entityType, Enum16.SomeValue, () => dt.Enum16);
                AssertEqualIfMapped(entityType, Enum8.SomeValue, () => dt.Enum8);
                AssertEqualIfMapped(entityType, (ushort)1234, () => dt.TestNullableUnsignedInt16);
                AssertEqualIfMapped(entityType, 1234565789U, () => dt.TestNullableUnsignedInt32);
                AssertEqualIfMapped(entityType, 1234567890123456789UL, () => dt.TestNullableUnsignedInt64);
                AssertEqualIfMapped(entityType, 'a', () => dt.TestNullableCharacter);
                AssertEqualIfMapped(entityType, (sbyte)-128, () => dt.TestNullableSignedByte);
                AssertEqualIfMapped(entityType, EnumU64.SomeValue, () => dt.EnumU64);
                AssertEqualIfMapped(entityType, EnumU32.SomeValue, () => dt.EnumU32);
                AssertEqualIfMapped(entityType, EnumU16.SomeValue, () => dt.EnumU16);
                AssertEqualIfMapped(entityType, EnumS8.SomeValue, () => dt.EnumS8);
            }
        }

        public override void Can_insert_and_read_back_object_backed_data_types()
        {
            using (var context = CreateContext())
            {
                context.Set<ObjectBackedDataTypes>().Add(
                    new ObjectBackedDataTypes
                    {
                        Id = 101,
                        PartitionId = 101,
                        String = "TestString",
                        Bytes = new byte[] { 10, 9, 8, 7, 6 },
                        Int16 = -1234,
                        Int32 = -123456789,
                        Int64 = -1234567890123456789L,
                        Double = -1.23456789,
                        Decimal = -1234567890.01M,
                        DateTime = DateTime.Parse("01/01/2000 12:34:56"),
                        DateTimeOffset = new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0)),
                        TimeSpan = new TimeSpan(0, 10, 9, 8, 0),
                        DateOnly = new DateOnly(2020, 3, 1),
                        TimeOnly = new TimeOnly(12, 30, 45, 0),
                        Single = -1.234F,
                        Boolean = false,
                        Byte = 255,
                        UnsignedInt16 = 1234,
                        UnsignedInt32 = 1234565789U,
                        UnsignedInt64 = 1234567890123456789UL,
                        Character = 'a',
                        SignedByte = -128,
                        Enum64 = Enum64.SomeValue,
                        Enum32 = Enum32.SomeValue,
                        Enum16 = Enum16.SomeValue,
                        Enum8 = Enum8.SomeValue,
                        EnumU64 = EnumU64.SomeValue,
                        EnumU32 = EnumU32.SomeValue,
                        EnumU16 = EnumU16.SomeValue,
                        EnumS8 = EnumS8.SomeValue
                    });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                var dt = context.Set<ObjectBackedDataTypes>().Where(ndt => ndt.Id == 101).ToList().Single();

                var entityType = context.Model.FindEntityType(typeof(ObjectBackedDataTypes));
                AssertEqualIfMapped(entityType, "TestString", () => dt.String);
                AssertEqualIfMapped(entityType, new byte[] { 10, 9, 8, 7, 6 }, () => dt.Bytes);
                AssertEqualIfMapped(entityType, (short)-1234, () => dt.Int16);
                AssertEqualIfMapped(entityType, -123456789, () => dt.Int32);
                AssertEqualIfMapped(entityType, -1234567890123456789L, () => dt.Int64);
                AssertEqualIfMapped(entityType, -1.23456789, () => dt.Double);
                AssertEqualIfMapped(entityType, -1234567890.01M, () => dt.Decimal);
                AssertEqualIfMapped(entityType, DateTime.Parse("01/01/2000 12:34:56"), () => dt.DateTime);
                AssertEqualIfMapped(
                    entityType, JetTestHelpers.GetExpectedValue(new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0))),
                    () => dt.DateTimeOffset);
                AssertEqualIfMapped(entityType, new TimeSpan(0, 10, 9, 8, 0), () => dt.TimeSpan);
                AssertEqualIfMapped(entityType, new DateOnly(2020, 3, 1), () => dt.DateOnly);
                AssertEqualIfMapped(entityType, new TimeOnly(12, 30, 45, 0), () => dt.TimeOnly);
                AssertEqualIfMapped(entityType, -1.234F, () => dt.Single);
                AssertEqualIfMapped(entityType, false, () => dt.Boolean);
                AssertEqualIfMapped(entityType, (byte)255, () => dt.Byte);
                AssertEqualIfMapped(entityType, Enum64.SomeValue, () => dt.Enum64);
                AssertEqualIfMapped(entityType, Enum32.SomeValue, () => dt.Enum32);
                AssertEqualIfMapped(entityType, Enum16.SomeValue, () => dt.Enum16);
                AssertEqualIfMapped(entityType, Enum8.SomeValue, () => dt.Enum8);
                AssertEqualIfMapped(entityType, (ushort)1234, () => dt.UnsignedInt16);
                AssertEqualIfMapped(entityType, 1234565789U, () => dt.UnsignedInt32);
                AssertEqualIfMapped(entityType, 1234567890123456789UL, () => dt.UnsignedInt64);
                AssertEqualIfMapped(entityType, 'a', () => dt.Character);
                AssertEqualIfMapped(entityType, (sbyte)-128, () => dt.SignedByte);
                AssertEqualIfMapped(entityType, EnumU64.SomeValue, () => dt.EnumU64);
                AssertEqualIfMapped(entityType, EnumU32.SomeValue, () => dt.EnumU32);
                AssertEqualIfMapped(entityType, EnumU16.SomeValue, () => dt.EnumU16);
                AssertEqualIfMapped(entityType, EnumS8.SomeValue, () => dt.EnumS8);
            }
        }

        public override void Can_insert_and_read_back_nullable_backed_data_types()
        {
            using (var context = CreateContext())
            {
                context.Set<NullableBackedDataTypes>().Add(
                    new NullableBackedDataTypes
                    {
                        Id = 101,
                        PartitionId = 101,
                        Int16 = -1234,
                        Int32 = -123456789,
                        Int64 = -1234567890123456789L,
                        Double = -1.23456789,
                        Decimal = -1234567890.01M,
                        DateTime = DateTime.Parse("01/01/2000 12:34:56"),
                        DateTimeOffset = new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0)),
                        TimeSpan = new TimeSpan(0, 10, 9, 8, 0),
                        DateOnly = new DateOnly(2020, 3, 1),
                        TimeOnly = new TimeOnly(12, 30, 45, 0),
                        Single = -1.234F,
                        Boolean = false,
                        Byte = 255,
                        UnsignedInt16 = 1234,
                        UnsignedInt32 = 1234565789U,
                        UnsignedInt64 = 1234567890123456789UL,
                        Character = 'a',
                        SignedByte = -128,
                        Enum64 = Enum64.SomeValue,
                        Enum32 = Enum32.SomeValue,
                        Enum16 = Enum16.SomeValue,
                        Enum8 = Enum8.SomeValue,
                        EnumU64 = EnumU64.SomeValue,
                        EnumU32 = EnumU32.SomeValue,
                        EnumU16 = EnumU16.SomeValue,
                        EnumS8 = EnumS8.SomeValue
                    });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                var dt = context.Set<NullableBackedDataTypes>().Where(ndt => ndt.Id == 101).ToList().Single();

                var entityType = context.Model.FindEntityType(typeof(NullableBackedDataTypes));
                AssertEqualIfMapped(entityType, (short)-1234, () => dt.Int16);
                AssertEqualIfMapped(entityType, -123456789, () => dt.Int32);
                AssertEqualIfMapped(entityType, -1234567890123456789L, () => dt.Int64);
                AssertEqualIfMapped(entityType, -1.23456789, () => dt.Double);
                AssertEqualIfMapped(entityType, -1234567890.01M, () => dt.Decimal);
                AssertEqualIfMapped(entityType, DateTime.Parse("01/01/2000 12:34:56"), () => dt.DateTime);
                AssertEqualIfMapped(
                    entityType, JetTestHelpers.GetExpectedValue(new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0))),
                    () => dt.DateTimeOffset);
                AssertEqualIfMapped(entityType, new TimeSpan(0, 10, 9, 8, 0), () => dt.TimeSpan);
                AssertEqualIfMapped(entityType, new DateOnly(2020, 3, 1), () => dt.DateOnly);
                AssertEqualIfMapped(entityType, new TimeOnly(12, 30, 45, 0), () => dt.TimeOnly);
                AssertEqualIfMapped(entityType, -1.234F, () => dt.Single);
                AssertEqualIfMapped(entityType, false, () => dt.Boolean);
                AssertEqualIfMapped(entityType, (byte)255, () => dt.Byte);
                AssertEqualIfMapped(entityType, Enum64.SomeValue, () => dt.Enum64);
                AssertEqualIfMapped(entityType, Enum32.SomeValue, () => dt.Enum32);
                AssertEqualIfMapped(entityType, Enum16.SomeValue, () => dt.Enum16);
                AssertEqualIfMapped(entityType, Enum8.SomeValue, () => dt.Enum8);
                AssertEqualIfMapped(entityType, (ushort)1234, () => dt.UnsignedInt16);
                AssertEqualIfMapped(entityType, 1234565789U, () => dt.UnsignedInt32);
                AssertEqualIfMapped(entityType, 1234567890123456789UL, () => dt.UnsignedInt64);
                AssertEqualIfMapped(entityType, 'a', () => dt.Character);
                AssertEqualIfMapped(entityType, (sbyte)-128, () => dt.SignedByte);
                AssertEqualIfMapped(entityType, EnumU64.SomeValue, () => dt.EnumU64);
                AssertEqualIfMapped(entityType, EnumU32.SomeValue, () => dt.EnumU32);
                AssertEqualIfMapped(entityType, EnumU16.SomeValue, () => dt.EnumU16);
                AssertEqualIfMapped(entityType, EnumS8.SomeValue, () => dt.EnumS8);
            }
        }

        public override void Can_insert_and_read_back_non_nullable_backed_data_types()
        {
            using (var context = CreateContext())
            {
                context.Set<NonNullableBackedDataTypes>().Add(
                    new NonNullableBackedDataTypes
                    {
                        Id = 101,
                        PartitionId = 101,
                        Int16 = -1234,
                        Int32 = -123456789,
                        Int64 = -1234567890123456789L,
                        Double = -1.23456789,
                        Decimal = -1234567890.01M,
                        DateTime = DateTime.Parse("01/01/2000 12:34:56"),
                        DateTimeOffset = new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0)),
                        TimeSpan = new TimeSpan(0, 10, 9, 8, 0),
                        DateOnly = new DateOnly(2020, 3, 1),
                        TimeOnly = new TimeOnly(12, 30, 45, 0),
                        Single = -1.234F,
                        Boolean = true,
                        Byte = 255,
                        UnsignedInt16 = 1234,
                        UnsignedInt32 = 1234565789U,
                        UnsignedInt64 = 1234567890123456789UL,
                        Character = 'a',
                        SignedByte = -128,
                        Enum64 = Enum64.SomeValue,
                        Enum32 = Enum32.SomeValue,
                        Enum16 = Enum16.SomeValue,
                        Enum8 = Enum8.SomeValue,
                        EnumU64 = EnumU64.SomeValue,
                        EnumU32 = EnumU32.SomeValue,
                        EnumU16 = EnumU16.SomeValue,
                        EnumS8 = EnumS8.SomeValue
                    });

                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = CreateContext())
            {
                var dt = context.Set<NonNullableBackedDataTypes>().Where(ndt => ndt.Id == 101).ToList().Single();

                var entityType = context.Model.FindEntityType(typeof(NonNullableBackedDataTypes));
                AssertEqualIfMapped(entityType, (short)-1234, () => dt.Int16);
                AssertEqualIfMapped(entityType, -123456789, () => dt.Int32);
                AssertEqualIfMapped(entityType, -1234567890123456789L, () => dt.Int64);
                AssertEqualIfMapped(entityType, -1234567890123456789L, () => dt.Int64);
                AssertEqualIfMapped(entityType, -1.23456789, () => dt.Double);
                AssertEqualIfMapped(entityType, -1234567890.01M, () => dt.Decimal);
                AssertEqualIfMapped(entityType, DateTime.Parse("01/01/2000 12:34:56"), () => dt.DateTime);
                AssertEqualIfMapped(
                    entityType, JetTestHelpers.GetExpectedValue(new DateTimeOffset(DateTime.Parse("01/01/2000 12:34:56"), TimeSpan.FromHours(-8.0))),
                    () => dt.DateTimeOffset);
                AssertEqualIfMapped(entityType, new TimeSpan(0, 10, 9, 8, 0), () => dt.TimeSpan);
                AssertEqualIfMapped(entityType, new DateOnly(2020, 3, 1), () => dt.DateOnly);
                AssertEqualIfMapped(entityType, new TimeOnly(12, 30, 45, 0), () => dt.TimeOnly);
                AssertEqualIfMapped(entityType, -1.234F, () => dt.Single);
                AssertEqualIfMapped(entityType, true, () => dt.Boolean);
                AssertEqualIfMapped(entityType, (byte)255, () => dt.Byte);
                AssertEqualIfMapped(entityType, Enum64.SomeValue, () => dt.Enum64);
                AssertEqualIfMapped(entityType, Enum32.SomeValue, () => dt.Enum32);
                AssertEqualIfMapped(entityType, Enum16.SomeValue, () => dt.Enum16);
                AssertEqualIfMapped(entityType, Enum8.SomeValue, () => dt.Enum8);
                AssertEqualIfMapped(entityType, (ushort)1234, () => dt.UnsignedInt16);
                AssertEqualIfMapped(entityType, 1234565789U, () => dt.UnsignedInt32);
                AssertEqualIfMapped(entityType, 1234567890123456789UL, () => dt.UnsignedInt64);
                AssertEqualIfMapped(entityType, 'a', () => dt.Character);
                AssertEqualIfMapped(entityType, (sbyte)-128, () => dt.SignedByte);
                AssertEqualIfMapped(entityType, EnumU64.SomeValue, () => dt.EnumU64);
                AssertEqualIfMapped(entityType, EnumU32.SomeValue, () => dt.EnumU32);
                AssertEqualIfMapped(entityType, EnumU16.SomeValue, () => dt.EnumU16);
                AssertEqualIfMapped(entityType, EnumS8.SomeValue, () => dt.EnumS8);
            }
        }

        public override void Can_query_using_any_data_type()
        {
            using var context = CreateContext();
            var source = AddTestBuiltInDataTypes(context.Set<BuiltInDataTypes>());

            Assert.Equal(1, context.SaveChanges());

            QueryBuiltInDataTypesTest(source);
        }

        public override void Can_query_using_any_data_type_shadow()
        {
            using var context = CreateContext();
            var source = AddTestBuiltInDataTypes(context.Set<BuiltInDataTypesShadow>());

            Assert.Equal(1, context.SaveChanges());

            QueryBuiltInDataTypesTest(source);
        }

        private void QueryBuiltInDataTypesTest<TEntity>(EntityEntry<TEntity> source)
            where TEntity : BuiltInDataTypesBase
        {
            using var context = CreateContext();
            var set = context.Set<TEntity>();
            var entity = set.Where(e => e.Id == 11).ToList().Single();
            var entityType = context.Model.FindEntityType(typeof(TEntity));

            var param1 = (short)-1234;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<short>(e, nameof(BuiltInDataTypes.TestInt16)) == param1).ToList().Single());

            var param2 = -123456789;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<int>(e, nameof(BuiltInDataTypes.TestInt32)) == param2).ToList().Single());

            var param3 = -1234567890123456789L;
            if (Fixture.IntegerPrecision == 64)
            {
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<long>(e, nameof(BuiltInDataTypes.TestInt64)) == param3).ToList().Single());
            }

            double? param4 = -1.23456789;
            if (Fixture.StrictEquality)
            {
                Assert.Same(
                    entity, set.Where(
                        e => e.Id == 11
                            && EF.Property<double>(e, nameof(BuiltInDataTypes.TestDouble)) == param4).ToList().Single());
            }
            else if (Fixture.SupportsDecimalComparisons)
            {
                double? param4l = -1.234567891;
                double? param4h = -1.234567889;
                Assert.Same(
                    entity, set.Where(
                            e => e.Id == 11
                                && (EF.Property<double>(e, nameof(BuiltInDataTypes.TestDouble)) == param4
                                    || (EF.Property<double>(e, nameof(BuiltInDataTypes.TestDouble)) > param4l
                                        && EF.Property<double>(e, nameof(BuiltInDataTypes.TestDouble)) < param4h)))
                        .ToList().Single());
            }

            var param5 = -1234567890.01M;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<decimal>(e, nameof(BuiltInDataTypes.TestDecimal)) == param5).ToList()
                    .Single());

            var param6 = Fixture.DefaultDateTime;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<DateTime>(e, nameof(BuiltInDataTypes.TestDateTime)) == param6).ToList()
                    .Single());

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestDateTimeOffset)) != null)
            {
                var param7 = new DateTimeOffset(new DateTime(), TimeSpan.FromHours(-8.0));
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<DateTimeOffset>(e, nameof(BuiltInDataTypes.TestDateTimeOffset)) == param7)
                        .ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestTimeSpan)) != null)
            {
                var param8 = new TimeSpan(0, 10, 9, 8, 7);
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<TimeSpan>(e, nameof(BuiltInDataTypes.TestTimeSpan)) == param8).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestDateOnly)) != null)
            {
                var param9 = new DateOnly(2020, 3, 1);
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<DateOnly>(e, nameof(BuiltInDataTypes.TestDateOnly)) == param9).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestTimeOnly)) != null)
            {
                var param10 = new TimeOnly(12, 30, 45, 0);
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<TimeOnly>(e, nameof(BuiltInDataTypes.TestTimeOnly)) == param10).ToList()
                        .Single());
            }

            var param11 = -1.234F;
            if (Fixture.StrictEquality)
            {
                Assert.Same(
                    entity, set.Where(
                        e => e.Id == 11
                            && EF.Property<float>(e, nameof(BuiltInDataTypes.TestSingle)) == param11).ToList().Single());
            }
            else if (Fixture.SupportsDecimalComparisons)
            {
                var param11l = -1.2341F;
                var param11h = -1.2339F;
                Assert.Same(
                    entity, set.Where(
                        e => e.Id == 11
                            && (EF.Property<float>(e, nameof(BuiltInDataTypes.TestSingle)) == param11
                                || (EF.Property<float>(e, nameof(BuiltInDataTypes.TestSingle)) > param11l
                                    && EF.Property<float>(e, nameof(BuiltInDataTypes.TestSingle)) < param11h))).ToList().Single());
            }

            var param12 = true;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<bool>(e, nameof(BuiltInDataTypes.TestBoolean)) == param12).ToList().Single());

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestByte)) != null)
            {
                var param13 = (byte)255;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<byte>(e, nameof(BuiltInDataTypes.TestByte)) == param13).ToList().Single());
            }

            var param14 = Enum64.SomeValue;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<Enum64>(e, nameof(BuiltInDataTypes.Enum64)) == param14).ToList().Single());

            var param15 = Enum32.SomeValue;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<Enum32>(e, nameof(BuiltInDataTypes.Enum32)) == param15).ToList().Single());

            var param16 = Enum16.SomeValue;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<Enum16>(e, nameof(BuiltInDataTypes.Enum16)) == param16).ToList().Single());

            if (entityType.FindProperty(nameof(BuiltInDataTypes.Enum8)) != null)
            {
                var param17 = Enum8.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum8>(e, nameof(BuiltInDataTypes.Enum8)) == param17).ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestUnsignedInt16)) != null)
            {
                var param18 = (ushort)1234;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<ushort>(e, nameof(BuiltInDataTypes.TestUnsignedInt16)) == param18).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestUnsignedInt32)) != null)
            {
                var param19 = 1234565789U;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<uint>(e, nameof(BuiltInDataTypes.TestUnsignedInt32)) == param19).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestUnsignedInt64)) != null)
            {
                var param20 = 1234567890123456789UL;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<ulong>(e, nameof(BuiltInDataTypes.TestUnsignedInt64)) == param20).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestCharacter)) != null)
            {
                var param21 = 'a';
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<char>(e, nameof(BuiltInDataTypes.TestCharacter)) == param21).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.TestSignedByte)) != null)
            {
                var param22 = (sbyte)-128;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<sbyte>(e, nameof(BuiltInDataTypes.TestSignedByte)) == param22).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.EnumU64)) != null)
            {
                var param23 = EnumU64.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumU64>(e, nameof(BuiltInDataTypes.EnumU64)) == param23).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.EnumU32)) != null)
            {
                var param24 = EnumU32.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumU32>(e, nameof(BuiltInDataTypes.EnumU32)) == param24).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.EnumU16)) != null)
            {
                var param25 = EnumU16.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumU16>(e, nameof(BuiltInDataTypes.EnumU16)) == param25).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInDataTypes.EnumS8)) != null)
            {
                var param26 = EnumS8.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumS8>(e, nameof(BuiltInDataTypes.EnumS8)) == param26).ToList().Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInDataTypes.Enum64))?.GetProviderClrType()) == typeof(long))
            {
                var param27 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum64>(e, nameof(BuiltInDataTypes.Enum64)) == (Enum64)param27).ToList()
                        .Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum64>(e, nameof(BuiltInDataTypes.Enum64)) == param27).ToList()
                        .Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInDataTypes.Enum32))?.GetProviderClrType()) == typeof(int))
            {
                var param28 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum32>(e, nameof(BuiltInDataTypes.Enum32)) == (Enum32)param28).ToList()
                        .Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum32>(e, nameof(BuiltInDataTypes.Enum32)) == param28).ToList()
                        .Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInDataTypes.Enum16))?.GetProviderClrType()) == typeof(short))
            {
                var param29 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum16>(e, nameof(BuiltInDataTypes.Enum16)) == (Enum16)param29).ToList()
                        .Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum16>(e, nameof(BuiltInDataTypes.Enum16)) == param29).ToList()
                        .Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInDataTypes.Enum8))?.GetProviderClrType()) == typeof(byte))
            {
                var param30 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum8>(e, nameof(BuiltInDataTypes.Enum8)) == (Enum8)param30).ToList()
                        .Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum8>(e, nameof(BuiltInDataTypes.Enum8)) == param30).ToList()
                        .Single());
            }

            foreach (var propertyEntry in context.Entry(entity).Properties)
            {
                if (propertyEntry.Metadata.ValueGenerated != ValueGenerated.Never)
                {
                    continue;
                }

                Assert.Equal(
                    source.Property(propertyEntry.Metadata).CurrentValue,
                    propertyEntry.CurrentValue);
            }
        }

        protected new EntityEntry<TEntity> AddTestBuiltInDataTypes<TEntity>(DbSet<TEntity> set)
            where TEntity : BuiltInDataTypesBase, new()
        {
            var entityEntry = set.Add(
                new TEntity { Id = 11 });

            entityEntry.CurrentValues.SetValues(
                new BuiltInDataTypes
                {
                    Id = 11,
                    PartitionId = 1,
                    TestInt16 = -1234,
                    TestInt32 = -123456789,
                    TestInt64 = -1234567890123456789L,
                    TestDouble = -1.23456789,
                    TestDecimal = -1234567890.01M,
                    TestDateTime = Fixture.DefaultDateTime,
                    TestDateTimeOffset = new DateTimeOffset(new DateTime(), TimeSpan.FromHours(-8.0)),
                    TestTimeSpan = new TimeSpan(0, 10, 9, 8, 7),
                    TestDateOnly = new DateOnly(2020, 3, 1),
                    TestTimeOnly = new TimeOnly(12, 30, 45, 0),
                    TestSingle = -1.234F,
                    TestBoolean = true,
                    TestByte = 255,
                    TestUnsignedInt16 = 1234,
                    TestUnsignedInt32 = 1234565789U,
                    TestUnsignedInt64 = 1234567890123456789UL,
                    TestCharacter = 'a',
                    TestSignedByte = -128,
                    Enum64 = Enum64.SomeValue,
                    Enum32 = Enum32.SomeValue,
                    Enum16 = Enum16.SomeValue,
                    Enum8 = Enum8.SomeValue,
                    EnumU64 = EnumU64.SomeValue,
                    EnumU32 = EnumU32.SomeValue,
                    EnumU16 = EnumU16.SomeValue,
                    EnumS8 = EnumS8.SomeValue
                });

            return entityEntry;
        }

        public override void Can_query_using_any_nullable_data_type()
        {
            using var context = CreateContext();
            var source = AddTestBuiltInNullableDataTypes(context.Set<BuiltInNullableDataTypes>());

            Assert.Equal(1, context.SaveChanges());

            QueryBuiltInNullableDataTypesTest(source);
        }

        public override void Can_query_using_any_data_type_nullable_shadow()
        {
            using var context = CreateContext();
            var source = AddTestBuiltInNullableDataTypes(context.Set<BuiltInNullableDataTypesShadow>());

            Assert.Equal(1, context.SaveChanges());

            QueryBuiltInNullableDataTypesTest(source);
        }

        private void QueryBuiltInNullableDataTypesTest<TEntity>(EntityEntry<TEntity> source)
            where TEntity : BuiltInNullableDataTypesBase
        {
            using var context = CreateContext();
            var set = context.Set<TEntity>();
            var entity = set.Where(e => e.Id == 11).ToList().Single();
            var entityType = context.Model.FindEntityType(typeof(TEntity));

            short? param1 = -1234;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<short?>(e, nameof(BuiltInNullableDataTypes.TestNullableInt16)) == param1)
                    .ToList().Single());

            int? param2 = -123456789;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<int?>(e, nameof(BuiltInNullableDataTypes.TestNullableInt32)) == param2)
                    .ToList().Single());

            long? param3 = -1234567890123456789L;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<long?>(e, nameof(BuiltInNullableDataTypes.TestNullableInt64)) == param3)
                    .ToList().Single());

            double? param4 = -1.23456789;
            if (Fixture.StrictEquality)
            {
                Assert.Same(
                    entity, set.Where(
                            e => e.Id == 11
                                && EF.Property<double?>(e, nameof(BuiltInNullableDataTypes.TestNullableDouble)) == param4).ToList()
                        .Single());
            }
            else if (Fixture.SupportsDecimalComparisons)
            {
                double? param4l = -1.234567891;
                double? param4h = -1.234567889;
                Assert.Same(
                    entity, set.Where(
                            e => e.Id == 11
                                && (EF.Property<double?>(e, nameof(BuiltInNullableDataTypes.TestNullableDouble)) == param4
                                    || (EF.Property<double?>(e, nameof(BuiltInNullableDataTypes.TestNullableDouble)) > param4l
                                        && EF.Property<double?>(e, nameof(BuiltInNullableDataTypes.TestNullableDouble)) < param4h)))
                        .ToList().Single());
            }

            decimal? param5 = -1234567890.01M;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<decimal?>(e, nameof(BuiltInNullableDataTypes.TestNullableDecimal)) == param5)
                    .ToList().Single());

            DateTime? param6 = Fixture.DefaultDateTime;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<DateTime?>(e, nameof(BuiltInNullableDataTypes.TestNullableDateTime)) == param6)
                    .ToList().Single());

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableDateTimeOffset)) != null)
            {
                DateTimeOffset? param7 = new DateTimeOffset(new DateTime(), TimeSpan.FromHours(-8.0));
                Assert.Same(
                    entity,
                    set.Where(
                        e => e.Id == 11
                            && EF.Property<DateTimeOffset?>(e, nameof(BuiltInNullableDataTypes.TestNullableDateTimeOffset))
                            == param7).ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableTimeSpan)) != null)
            {
                TimeSpan? param8 = new TimeSpan(0, 10, 9, 8, 7);
                Assert.Same(
                    entity,
                    set.Where(
                            e => e.Id == 11
                                && EF.Property<TimeSpan?>(e, nameof(BuiltInNullableDataTypes.TestNullableTimeSpan))
                                == param8)
                        .ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableDateOnly)) != null)
            {
                DateOnly? param9 = new DateOnly(2020, 3, 1);
                Assert.Same(
                    entity,
                    set.Where(
                            e => e.Id == 11
                                && EF.Property<DateOnly?>(e, nameof(BuiltInNullableDataTypes.TestNullableDateOnly))
                                == param9)
                        .ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableTimeOnly)) != null)
            {
                TimeOnly? param10 = new TimeOnly(12, 30, 45, 0);
                Assert.Same(
                    entity,
                    set.Where(
                            e => e.Id == 11
                                && EF.Property<TimeOnly?>(e, nameof(BuiltInNullableDataTypes.TestNullableTimeOnly))
                                == param10)
                        .ToList().Single());
            }

            float? param11 = -1.234F;
            if (Fixture.StrictEquality)
            {
                Assert.Same(
                    entity, set.Where(
                            e => e.Id == 11
                                && EF.Property<float?>(e, nameof(BuiltInNullableDataTypes.TestNullableSingle)) == param11).ToList()
                        .Single());
            }
            else if (Fixture.SupportsDecimalComparisons)
            {
                float? param11l = -1.2341F;
                float? param11h = -1.2339F;
                Assert.Same(
                    entity, set.Where(
                            e => e.Id == 11
                                && (EF.Property<float?>(e, nameof(BuiltInNullableDataTypes.TestNullableSingle)) == param11
                                    || (EF.Property<float?>(e, nameof(BuiltInNullableDataTypes.TestNullableSingle)) > param11l
                                        && EF.Property<float?>(e, nameof(BuiltInNullableDataTypes.TestNullableSingle)) < param11h)))
                        .ToList().Single());
            }

            bool? param12 = true;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<bool?>(e, nameof(BuiltInNullableDataTypes.TestNullableBoolean)) == param12)
                    .ToList().Single());

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableByte)) != null)
            {
                byte? param13 = 255;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<byte?>(e, nameof(BuiltInNullableDataTypes.TestNullableByte)) == param13)
                        .ToList().Single());
            }

            Enum64? param14 = Enum64.SomeValue;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<Enum64?>(e, nameof(BuiltInNullableDataTypes.Enum64)) == param14).ToList()
                    .Single());

            Enum32? param15 = Enum32.SomeValue;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<Enum32?>(e, nameof(BuiltInNullableDataTypes.Enum32)) == param15).ToList()
                    .Single());

            Enum16? param16 = Enum16.SomeValue;
            Assert.Same(
                entity,
                set.Where(e => e.Id == 11 && EF.Property<Enum16?>(e, nameof(BuiltInNullableDataTypes.Enum16)) == param16).ToList()
                    .Single());

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.Enum8)) != null)
            {
                Enum8? param17 = Enum8.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum8?>(e, nameof(BuiltInNullableDataTypes.Enum8)) == param17).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableUnsignedInt16)) != null)
            {
                ushort? param18 = 1234;
                Assert.Same(
                    entity,
                    set.Where(
                        e => e.Id == 11
                            && EF.Property<ushort?>(e, nameof(BuiltInNullableDataTypes.TestNullableUnsignedInt16))
                            == param18).ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableUnsignedInt32)) != null)
            {
                uint? param19 = 1234565789U;
                Assert.Same(
                    entity,
                    set.Where(
                            e => e.Id == 11
                                && EF.Property<uint?>(e, nameof(BuiltInNullableDataTypes.TestNullableUnsignedInt32))
                                == param19)
                        .ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableUnsignedInt64)) != null)
            {
                ulong? param20 = 1234567890123456789UL;
                Assert.Same(
                    entity,
                    set.Where(
                        e => e.Id == 11
                            && EF.Property<ulong?>(
                                e, nameof(BuiltInNullableDataTypes.TestNullableUnsignedInt64))
                            == param20).ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableCharacter)) != null)
            {
                char? param21 = 'a';
                Assert.Same(
                    entity,
                    set.Where(
                            e => e.Id == 11 && EF.Property<char?>(e, nameof(BuiltInNullableDataTypes.TestNullableCharacter)) == param21)
                        .ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.TestNullableSignedByte)) != null)
            {
                sbyte? param22 = -128;
                Assert.Same(
                    entity,
                    set.Where(
                            e => e.Id == 11
                                && EF.Property<sbyte?>(e, nameof(BuiltInNullableDataTypes.TestNullableSignedByte))
                                == param22)
                        .ToList().Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.EnumU64)) != null)
            {
                var param23 = EnumU64.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumU64?>(e, nameof(BuiltInNullableDataTypes.EnumU64)) == param23).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.EnumU32)) != null)
            {
                var param24 = EnumU32.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumU32?>(e, nameof(BuiltInNullableDataTypes.EnumU32)) == param24).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.EnumU16)) != null)
            {
                var param25 = EnumU16.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumU16?>(e, nameof(BuiltInNullableDataTypes.EnumU16)) == param25).ToList()
                        .Single());
            }

            if (entityType.FindProperty(nameof(BuiltInNullableDataTypes.EnumS8)) != null)
            {
                var param26 = EnumS8.SomeValue;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<EnumS8?>(e, nameof(BuiltInNullableDataTypes.EnumS8)) == param26).ToList()
                        .Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInNullableDataTypes.Enum64))?.GetProviderClrType())
                == typeof(long))
            {
                int? param27 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum64?>(e, nameof(BuiltInNullableDataTypes.Enum64)) == (Enum64)param27)
                        .ToList().Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum64?>(e, nameof(BuiltInNullableDataTypes.Enum64)) == param27)
                        .ToList().Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInNullableDataTypes.Enum32))?.GetProviderClrType())
                == typeof(int))
            {
                int? param28 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum32?>(e, nameof(BuiltInNullableDataTypes.Enum32)) == (Enum32)param28)
                        .ToList().Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum32?>(e, nameof(BuiltInNullableDataTypes.Enum32)) == param28)
                        .ToList().Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInNullableDataTypes.Enum16))?.GetProviderClrType())
                == typeof(short))
            {
                int? param29 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum16?>(e, nameof(BuiltInNullableDataTypes.Enum16)) == (Enum16)param29)
                        .ToList().Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum16?>(e, nameof(BuiltInNullableDataTypes.Enum16)) == param29)
                        .ToList().Single());
            }

            if (UnwrapNullableType(entityType.FindProperty(nameof(BuiltInNullableDataTypes.Enum8))?.GetProviderClrType())
                == typeof(byte))
            {
                int? param30 = 1;
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && EF.Property<Enum8?>(e, nameof(BuiltInNullableDataTypes.Enum8)) == (Enum8)param30)
                        .ToList().Single());
                Assert.Same(
                    entity,
                    set.Where(e => e.Id == 11 && (int)EF.Property<Enum8?>(e, nameof(BuiltInNullableDataTypes.Enum8)) == param30)
                        .ToList().Single());
            }

            foreach (var propertyEntry in context.Entry(entity).Properties)
            {
                if (propertyEntry.Metadata.ValueGenerated != ValueGenerated.Never)
                {
                    continue;
                }

                Assert.Equal(
                    source.Property(propertyEntry.Metadata).CurrentValue,
                    propertyEntry.CurrentValue);
            }
        }

        protected new virtual EntityEntry<TEntity> AddTestBuiltInNullableDataTypes<TEntity>(DbSet<TEntity> set)
            where TEntity : BuiltInNullableDataTypesBase, new()
        {
            var entityEntry = set.Add(
                new TEntity { Id = 11 });

            entityEntry.CurrentValues.SetValues(
                new BuiltInNullableDataTypes
                {
                    Id = 11,
                    PartitionId = 1,
                    TestNullableInt16 = -1234,
                    TestNullableInt32 = -123456789,
                    TestNullableInt64 = -1234567890123456789L,
                    TestNullableDouble = -1.23456789,
                    TestNullableDecimal = -1234567890.01M,
                    TestNullableDateTime = Fixture.DefaultDateTime,
                    TestNullableDateTimeOffset = new DateTimeOffset(new DateTime(), TimeSpan.FromHours(-8.0)),
                    TestNullableTimeSpan = new TimeSpan(0, 10, 9, 8, 7),
                    TestNullableDateOnly = new DateOnly(2020, 3, 1),
                    TestNullableTimeOnly = new TimeOnly(12, 30, 45, 0),
                    TestNullableSingle = -1.234F,
                    TestNullableBoolean = true,
                    TestNullableByte = 255,
                    TestNullableUnsignedInt16 = 1234,
                    TestNullableUnsignedInt32 = 1234565789U,
                    TestNullableUnsignedInt64 = 1234567890123456789UL,
                    TestNullableCharacter = 'a',
                    TestNullableSignedByte = -128,
                    Enum64 = Enum64.SomeValue,
                    Enum32 = Enum32.SomeValue,
                    Enum16 = Enum16.SomeValue,
                    Enum8 = Enum8.SomeValue,
                    EnumU64 = EnumU64.SomeValue,
                    EnumU32 = EnumU32.SomeValue,
                    EnumU16 = EnumU16.SomeValue,
                    EnumS8 = EnumS8.SomeValue
                });

            return entityEntry;
        }



        private void AssertEqualIfMapped<T>(IEntityType entityType, T expected, Expression<Func<T>> actualExpression)
        {
            if (entityType.FindProperty(((MemberExpression)actualExpression.Body).Member.Name) != null)
            {
                var actual = actualExpression.Compile()();
                var type = UnwrapNullableEnumType(typeof(T));
                if (IsSignedInteger(type))
                {
                    Assert.True(Equal(Convert.ToInt64(expected), Convert.ToInt64(actual)), $"Expected:\t{expected}\r\nActual:\t{actual}");
                }
                else if (IsUnsignedInteger(type))
                {
                    Assert.True(Equal(Convert.ToUInt64(expected), Convert.ToUInt64(actual)), $"Expected:\t{expected}\r\nActual:\t{actual}");
                }
                else if (type == typeof(DateTime))
                {
                    Assert.True(
                        Equal((DateTime)(object)expected, (DateTime)(object)actual), $"Expected:\t{expected:O}\r\nActual:\t{actual:O}");
                }
                else if (type == typeof(DateTimeOffset))
                {
                    Assert.True(
                        Equal((DateTimeOffset)(object)expected, (DateTimeOffset)(object)actual),
                        $"Expected:\t{expected:O}\r\nActual:\t{actual:O}");
                }
                else
                {
                    Assert.Equal(expected, actual);
                }
            }
        }

        private bool Equal(long left, long right)
        {
            if (left >= 0
                && right >= 0)
            {
                return Equal((ulong)left, (ulong)right);
            }

            if (left < 0
                && right < 0)
            {
                return Equal((ulong)-left, (ulong)-right);
            }

            return false;
        }

        private bool Equal(ulong left, ulong right)
        {
            if (Fixture.IntegerPrecision < 64)
            {
                var largestPrecise = 1ul << Fixture.IntegerPrecision;
                while (left > largestPrecise)
                {
                    left >>= 1;
                    right >>= 1;
                }
            }

            return left == right;
        }

        private bool Equal(DateTime left, DateTime right)
            => left.Equals(right) && (!Fixture.PreservesDateTimeKind || left.Kind == right.Kind);

        private bool Equal(DateTimeOffset left, DateTimeOffset right)
            => left.EqualsExact(right);

        private static Type UnwrapNullableType(Type type)
            => type == null ? null : Nullable.GetUnderlyingType(type) ?? type;

        private static bool IsSignedInteger(Type type)
            => type == typeof(int)
                || type == typeof(long)
                || type == typeof(short)
                || type == typeof(sbyte);

        private static bool IsUnsignedInteger(Type type)
            => type == typeof(byte)
                || type == typeof(uint)
                || type == typeof(ulong)
                || type == typeof(ushort)
                || type == typeof(char);

        public class ConvertToProviderTypesJetFixture : ConvertToProviderTypesFixtureBase
        {
            public override bool StrictEquality => true;

            public override bool SupportsAnsi => true;

            public override bool SupportsUnicodeToAnsiConversion => true;

            public override bool SupportsLargeStringComparisons => true;

            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

            public override bool SupportsBinaryKeys => true;

            public override bool SupportsDecimalComparisons => true;

            public override DateTime DefaultDateTime => new DateTime();
            public override bool PreservesDateTimeKind { get; }

            public override string ReallyLargeString
                => string.Join("", Enumerable.Repeat("testphrase", 25));

            public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
                => base
                    .AddOptions(builder)
                    .ConfigureWarnings(
                        c => c.Log(JetEventId.DecimalTypeDefaultWarning));

            protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
            {
                base.OnModelCreating(modelBuilder, context);

                modelBuilder.Entity<BuiltInDataTypes>().Property(e => e.Enum8).IsFixedLength();
            }
        }
    }
}
