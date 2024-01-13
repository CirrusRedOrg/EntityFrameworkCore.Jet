// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

        public class CustomConvertersJetFixture : CustomConvertersFixtureBase
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

                modelBuilder.Entity<BuiltInDataTypes>().Property(e => e.TestBoolean).IsFixedLength();
            }
        }
    }
}
