// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Linq.Expressions;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class CustomConvertersJetTest : CustomConvertersTestBase<CustomConvertersJetTest.CustomConvertersJetFixture>
    {
        public CustomConvertersJetTest(CustomConvertersJetFixture fixture)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
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
AnimalIdentification.Method ---> [integer]
BinaryForeignKeyDataType.BinaryKeyDataTypeId ---> [nullable varbinary] [MaxLength = 510]
BinaryForeignKeyDataType.Id ---> [integer]
BinaryKeyDataType.Ex ---> [nullable varchar] [MaxLength = 255]
BinaryKeyDataType.Id ---> [varbinary] [MaxLength = 510]
Blog.BlogId ---> [counter]
Blog.Discriminator ---> [varchar] [MaxLength = 8]
Blog.IndexerVisible ---> [varchar] [MaxLength = 3]
Blog.IsVisible ---> [varchar] [MaxLength = 1]
Blog.RssUrl ---> [nullable varchar] [MaxLength = 255]
Blog.Url ---> [nullable varchar] [MaxLength = 255]
Book.Id ---> [integer]
Book.Value ---> [nullable varchar] [MaxLength = 255]
BuiltInDataTypes.Enum16 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.Enum32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.Enum64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.Enum8 ---> [varchar] [MaxLength = 255]
BuiltInDataTypes.EnumS8 ---> [varchar] [MaxLength = 24]
BuiltInDataTypes.EnumU16 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.EnumU32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.EnumU64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.Id ---> [integer]
BuiltInDataTypes.PartitionId ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestBoolean ---> [char] [MaxLength = 4]
BuiltInDataTypes.TestByte ---> [integer]
BuiltInDataTypes.TestCharacter ---> [integer]
BuiltInDataTypes.TestDateOnly ---> [varchar] [MaxLength = 255]
BuiltInDataTypes.TestDateTime ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestDateTimeOffset ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestDecimal ---> [varbinary] [MaxLength = 16]
BuiltInDataTypes.TestDouble ---> [decimal] [Precision = 26 Scale = 16]
BuiltInDataTypes.TestInt16 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestInt32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestInt64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestSignedByte ---> [decimal] [Precision = 18 Scale = 2]
BuiltInDataTypes.TestSingle ---> [double]
BuiltInDataTypes.TestTimeOnly ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypes.TestTimeSpan ---> [double]
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
BuiltInDataTypesShadow.TestBoolean ---> [varchar] [MaxLength = 255]
BuiltInDataTypesShadow.TestByte ---> [integer]
BuiltInDataTypesShadow.TestCharacter ---> [integer]
BuiltInDataTypesShadow.TestDateOnly ---> [varchar] [MaxLength = 255]
BuiltInDataTypesShadow.TestDateTime ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestDateTimeOffset ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestDecimal ---> [varbinary] [MaxLength = 16]
BuiltInDataTypesShadow.TestDouble ---> [decimal] [Precision = 26 Scale = 16]
BuiltInDataTypesShadow.TestInt16 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestInt32 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestInt64 ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestSignedByte ---> [decimal] [Precision = 18 Scale = 2]
BuiltInDataTypesShadow.TestSingle ---> [double]
BuiltInDataTypesShadow.TestTimeOnly ---> [decimal] [Precision = 20 Scale = 0]
BuiltInDataTypesShadow.TestTimeSpan ---> [double]
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
BuiltInNullableDataTypes.TestNullableBoolean ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypes.TestNullableByte ---> [nullable integer]
BuiltInNullableDataTypes.TestNullableCharacter ---> [nullable integer]
BuiltInNullableDataTypes.TestNullableDateOnly ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypes.TestNullableDateTime ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableDateTimeOffset ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableDecimal ---> [nullable varbinary] [MaxLength = 16]
BuiltInNullableDataTypes.TestNullableDouble ---> [nullable decimal] [Precision = 26 Scale = 16]
BuiltInNullableDataTypes.TestNullableInt16 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableInt32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableInt64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableSignedByte ---> [nullable decimal] [Precision = 18 Scale = 2]
BuiltInNullableDataTypes.TestNullableSingle ---> [nullable double]
BuiltInNullableDataTypes.TestNullableTimeOnly ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableTimeSpan ---> [nullable double]
BuiltInNullableDataTypes.TestNullableUnsignedInt16 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableUnsignedInt32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestNullableUnsignedInt64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypes.TestString ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.Enum16 ---> [nullable smallint]
BuiltInNullableDataTypesShadow.Enum32 ---> [nullable integer]
BuiltInNullableDataTypesShadow.Enum64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.Enum8 ---> [nullable byte]
BuiltInNullableDataTypesShadow.EnumS8 ---> [nullable smallint]
BuiltInNullableDataTypesShadow.EnumU16 ---> [nullable integer]
BuiltInNullableDataTypesShadow.EnumU32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.EnumU64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.Id ---> [integer]
BuiltInNullableDataTypesShadow.PartitionId ---> [integer]
BuiltInNullableDataTypesShadow.TestByteArray ---> [nullable longbinary]
BuiltInNullableDataTypesShadow.TestNullableBoolean ---> [nullable smallint]
BuiltInNullableDataTypesShadow.TestNullableByte ---> [nullable byte]
BuiltInNullableDataTypesShadow.TestNullableCharacter ---> [nullable varchar] [MaxLength = 1]
BuiltInNullableDataTypesShadow.TestNullableDateOnly ---> [nullable datetime]
BuiltInNullableDataTypesShadow.TestNullableDateTime ---> [nullable datetime]
BuiltInNullableDataTypesShadow.TestNullableDateTimeOffset ---> [nullable datetime]
BuiltInNullableDataTypesShadow.TestNullableDecimal ---> [nullable decimal] [Precision = 18 Scale = 2]
BuiltInNullableDataTypesShadow.TestNullableDouble ---> [nullable double]
BuiltInNullableDataTypesShadow.TestNullableInt16 ---> [nullable smallint]
BuiltInNullableDataTypesShadow.TestNullableInt32 ---> [nullable integer]
BuiltInNullableDataTypesShadow.TestNullableInt64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestNullableSignedByte ---> [nullable smallint]
BuiltInNullableDataTypesShadow.TestNullableSingle ---> [nullable single]
BuiltInNullableDataTypesShadow.TestNullableTimeOnly ---> [nullable datetime]
BuiltInNullableDataTypesShadow.TestNullableTimeSpan ---> [nullable datetime]
BuiltInNullableDataTypesShadow.TestNullableUnsignedInt16 ---> [nullable integer]
BuiltInNullableDataTypesShadow.TestNullableUnsignedInt32 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestNullableUnsignedInt64 ---> [nullable decimal] [Precision = 20 Scale = 0]
BuiltInNullableDataTypesShadow.TestString ---> [nullable varchar] [MaxLength = 255]
CollectionEnum.Id ---> [counter]
CollectionEnum.Roles ---> [nullable varchar] [MaxLength = 255]
CollectionScalar.Id ---> [counter]
CollectionScalar.Tags ---> [nullable varchar] [MaxLength = 255]
Dashboard.Id ---> [counter]
Dashboard.Layouts ---> [nullable varchar] [MaxLength = 255]
Dashboard.Name ---> [nullable varchar] [MaxLength = 255]
DateTimeEnclosure.DateTimeOffset ---> [nullable datetime]
DateTimeEnclosure.Id ---> [counter]
EmailTemplate.Id ---> [guid]
EmailTemplate.TemplateType ---> [integer]
Entity.Id ---> [counter]
Entity.SomeEnum ---> [varchar] [MaxLength = 255]
EntityWithValueWrapper.Id ---> [counter]
EntityWithValueWrapper.Wrapper ---> [nullable varchar] [MaxLength = 255]
HolderClass.HoldingEnum ---> [integer]
HolderClass.Id ---> [counter]
Load.Fuel ---> [double]
Load.LoadId ---> [integer]
MaxLengthDataTypes.ByteArray5 ---> [nullable varbinary] [MaxLength = 7]
MaxLengthDataTypes.ByteArray9000 ---> [nullable longchar]
MaxLengthDataTypes.Id ---> [integer]
MaxLengthDataTypes.String3 ---> [nullable varchar] [MaxLength = 12]
MaxLengthDataTypes.String9000 ---> [nullable longbinary]
MaxLengthDataTypes.StringUnbounded ---> [nullable longbinary]
NonNullableDependent.Id ---> [integer]
NonNullableDependent.PrincipalId ---> [integer]
NullablePrincipal.Id ---> [integer]
Order.Id ---> [varchar] [MaxLength = 255]
Parent.Id ---> [counter]
Parent.OwnedWithConverter_Value ---> [nullable varchar] [MaxLength = 64]
Person.Id ---> [integer]
Person.Name ---> [nullable varchar] [MaxLength = 255]
Person.SSN ---> [nullable integer]
Post.BlogId ---> [nullable integer]
Post.PostId ---> [counter]
SimpleCounter.CounterId ---> [integer]
SimpleCounter.Discriminator ---> [nullable varchar] [MaxLength = 255]
SimpleCounter.IsTest ---> [smallint]
SimpleCounter.StyleKey ---> [nullable varchar] [MaxLength = 255]
StringEnclosure.Id ---> [counter]
StringEnclosure.Value ---> [nullable varchar] [MaxLength = 255]
StringForeignKeyDataType.Id ---> [integer]
StringForeignKeyDataType.StringKeyDataTypeId ---> [nullable varchar] [MaxLength = 255]
StringKeyDataType.Id ---> [varchar] [MaxLength = 255]
StringListDataType.Id ---> [integer]
StringListDataType.Strings ---> [nullable varchar] [MaxLength = 255]
UnicodeDataTypes.Id ---> [integer]
UnicodeDataTypes.StringAnsi ---> [nullable varchar] [MaxLength = 255]
UnicodeDataTypes.StringAnsi3 ---> [nullable varchar] [MaxLength = 3]
UnicodeDataTypes.StringAnsi9000 ---> [nullable longchar]
UnicodeDataTypes.StringDefault ---> [nullable varchar] [MaxLength = 255]
UnicodeDataTypes.StringUnicode ---> [nullable varchar] [MaxLength = 255]
User.Email ---> [nullable varchar] [MaxLength = 255]
User.Id ---> [guid]
User23059.Id ---> [counter]
User23059.IsSoftDeleted ---> [smallint]
User23059.MessageGroups ---> [nullable varchar] [MaxLength = 255]
";

            Assert.Equal(expected, actual, ignoreLineEndingDifferences: true);
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

        [ConditionalFact]
        public override void Value_conversion_is_appropriately_used_for_join_condition()
        {
            base.Value_conversion_is_appropriately_used_for_join_condition();

            AssertSql(
                """
@__blogId_0='1'

SELECT [b].[Url]
FROM [Blog] AS [b]
INNER JOIN [Post] AS [p] ON [b].[BlogId] = [p].[BlogId] AND [b].[IsVisible] = N'Y' AND [b].[BlogId] = @__blogId_0
WHERE [b].[IsVisible] = N'Y'
""");
        }

        [ConditionalFact]
        public override void Value_conversion_is_appropriately_used_for_left_join_condition()
        {
            base.Value_conversion_is_appropriately_used_for_left_join_condition();

            AssertSql(
                """
@__blogId_0='1'

SELECT [b].[Url]
FROM [Blog] AS [b]
LEFT JOIN [Post] AS [p] ON [b].[BlogId] = [p].[BlogId] AND [b].[IsVisible] = N'Y' AND [b].[BlogId] = @__blogId_0
WHERE [b].[IsVisible] = N'Y'
""");
        }

        [ConditionalFact]
        public override void Where_bool_gets_converted_to_equality_when_value_conversion_is_used()
        {
            base.Where_bool_gets_converted_to_equality_when_value_conversion_is_used();

            AssertSql(
                """
SELECT `b`.`BlogId`, `b`.`Discriminator`, `b`.`IndexerVisible`, `b`.`IsVisible`, `b`.`Url`, `b`.`RssUrl`
FROM `Blog` AS `b`
WHERE `b`.`IsVisible` = 'Y'
""");
        }

        [ConditionalFact]
        public override void Where_negated_bool_gets_converted_to_equality_when_value_conversion_is_used()
        {
            base.Where_negated_bool_gets_converted_to_equality_when_value_conversion_is_used();

            AssertSql(
                """
SELECT `b`.`BlogId`, `b`.`Discriminator`, `b`.`IndexerVisible`, `b`.`IsVisible`, `b`.`Url`, `b`.`RssUrl`
FROM `Blog` AS `b`
WHERE `b`.`IsVisible` = 'N'
""");
        }

        public override void Where_bool_gets_converted_to_equality_when_value_conversion_is_used_using_EFProperty()
        {
            base.Where_bool_gets_converted_to_equality_when_value_conversion_is_used_using_EFProperty();

            AssertSql(
                """
SELECT `b`.`BlogId`, `b`.`Discriminator`, `b`.`IndexerVisible`, `b`.`IsVisible`, `b`.`Url`, `b`.`RssUrl`
FROM `Blog` AS `b`
WHERE `b`.`IsVisible` = 'Y'
""");
        }

        public override void Where_bool_gets_converted_to_equality_when_value_conversion_is_used_using_indexer()
        {
            base.Where_bool_gets_converted_to_equality_when_value_conversion_is_used_using_indexer();

            AssertSql(
                """
SELECT `b`.`BlogId`, `b`.`Discriminator`, `b`.`IndexerVisible`, `b`.`IsVisible`, `b`.`Url`, `b`.`RssUrl`
FROM `Blog` AS `b`
WHERE `b`.`IndexerVisible` = 'Nay'
""");
        }

        public override void Object_to_string_conversion()
        {
            // Return values are not string
        }

        public override void Id_object_as_entity_key()
        {
            base.Id_object_as_entity_key();

            AssertSql(
                """
SELECT `b`.`Id`, `b`.`Value`
FROM `Book` AS `b`
WHERE `b`.`Id` = 1
""");
        }

        public override void Infer_type_mapping_from_in_subquery_to_item()
        {
            base.Infer_type_mapping_from_in_subquery_to_item();

            AssertSql(
                """
SELECT `b`.`Id`, `b`.`Enum16`, `b`.`Enum32`, `b`.`Enum64`, `b`.`Enum8`, `b`.`EnumS8`, `b`.`EnumU16`, `b`.`EnumU32`, `b`.`EnumU64`, `b`.`PartitionId`, `b`.`TestBoolean`, `b`.`TestByte`, `b`.`TestCharacter`, `b`.`TestDateOnly`, `b`.`TestDateTime`, `b`.`TestDateTimeOffset`, `b`.`TestDecimal`, `b`.`TestDouble`, `b`.`TestInt16`, `b`.`TestInt32`, `b`.`TestInt64`, `b`.`TestSignedByte`, `b`.`TestSingle`, `b`.`TestTimeOnly`, `b`.`TestTimeSpan`, `b`.`TestUnsignedInt16`, `b`.`TestUnsignedInt32`, `b`.`TestUnsignedInt64`
FROM `BuiltInDataTypes` AS `b`
WHERE 'Yeps' IN (
    SELECT `b0`.`TestBoolean`
    FROM `BuiltInDataTypes` AS `b0`
) AND `b`.`Id` = 13
""");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        public override void Value_conversion_on_enum_collection_contains()
            => Assert.Contains(
                CoreStrings.TranslationFailed("")[47..],
                Assert.Throws<InvalidOperationException>(() => base.Value_conversion_on_enum_collection_contains()).Message);

        public class CustomConvertersJetFixture : CustomConvertersFixtureBase
        {
            public override bool StrictEquality => true;

            public override bool SupportsAnsi => false;

            public override bool SupportsUnicodeToAnsiConversion => false;

            public override bool SupportsLargeStringComparisons => true;

            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

            public TestSqlLoggerFactory TestSqlLoggerFactory
                => (TestSqlLoggerFactory)ListLoggerFactory;

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

                modelBuilder.Entity<BuiltInDataTypes>().Property(e => e.TestBoolean).IsFixedLength();
            }
        }
    }
}
