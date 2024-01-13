// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
