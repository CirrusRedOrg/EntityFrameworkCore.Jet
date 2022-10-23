// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Linq;

// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class EverythingIsStringsJetTest : BuiltInDataTypesTestBase<
        EverythingIsStringsJetTest.EverythingIsStringsJetFixture>
    {
        public EverythingIsStringsJetTest(EverythingIsStringsJetFixture fixture)
            : base(fixture)
        {
        }

        [ConditionalFact]
        public virtual void Columns_have_expected_data_types()
        {
            var actual = BuiltInDataTypesJetTest.QueryForColumnTypes(
                CreateContext(),
                nameof(ObjectBackedDataTypes),
                nameof(NullableBackedDataTypes),
                nameof(NonNullableBackedDataTypes),
                nameof(AnimalDetails));

            const string expected = @"#Dual.ID ---> [integer]
Animal.Id ---> [varchar] [MaxLength = 64]
AnimalIdentification.AnimalId ---> [varchar] [MaxLength = 64]
AnimalIdentification.Id ---> [varchar] [MaxLength = 64]
AnimalIdentification.Method ---> [varchar] [MaxLength = 255]
BinaryForeignKeyDataType.BinaryKeyDataTypeId ---> [nullable varchar] [MaxLength = 255]
BinaryForeignKeyDataType.Id ---> [varchar] [MaxLength = 64]
BinaryKeyDataType.Ex ---> [nullable varchar] [MaxLength = 255]
BinaryKeyDataType.Id ---> [varchar] [MaxLength = 255]
BuiltInDataTypes.Enum16 ---> [varchar] [MaxLength = 255]
BuiltInDataTypes.Enum32 ---> [varchar] [MaxLength = 255]
BuiltInDataTypes.Enum64 ---> [varchar] [MaxLength = 255]
BuiltInDataTypes.Enum8 ---> [varchar] [MaxLength = 255]
BuiltInDataTypes.EnumS8 ---> [varchar] [MaxLength = 255]
BuiltInDataTypes.EnumU16 ---> [varchar] [MaxLength = 255]
BuiltInDataTypes.EnumU32 ---> [varchar] [MaxLength = 255]
BuiltInDataTypes.EnumU64 ---> [varchar] [MaxLength = 255]
BuiltInDataTypes.Id ---> [varchar] [MaxLength = 64]
BuiltInDataTypes.PartitionId ---> [varchar] [MaxLength = 64]
BuiltInDataTypes.TestBoolean ---> [varchar] [MaxLength = 1]
BuiltInDataTypes.TestByte ---> [varchar] [MaxLength = 64]
BuiltInDataTypes.TestCharacter ---> [varchar] [MaxLength = 1]
BuiltInDataTypes.TestDateTime ---> [varchar] [MaxLength = 48]
BuiltInDataTypes.TestDateTimeOffset ---> [varchar] [MaxLength = 48]
BuiltInDataTypes.TestDecimal ---> [varchar] [MaxLength = 64]
BuiltInDataTypes.TestDouble ---> [varchar] [MaxLength = 64]
BuiltInDataTypes.TestInt16 ---> [varchar] [MaxLength = 64]
BuiltInDataTypes.TestInt32 ---> [varchar] [MaxLength = 64]
BuiltInDataTypes.TestInt64 ---> [varchar] [MaxLength = 64]
BuiltInDataTypes.TestSignedByte ---> [varchar] [MaxLength = 64]
BuiltInDataTypes.TestSingle ---> [varchar] [MaxLength = 64]
BuiltInDataTypes.TestTimeSpan ---> [varchar] [MaxLength = 48]
BuiltInDataTypes.TestUnsignedInt16 ---> [varchar] [MaxLength = 64]
BuiltInDataTypes.TestUnsignedInt32 ---> [varchar] [MaxLength = 64]
BuiltInDataTypes.TestUnsignedInt64 ---> [varchar] [MaxLength = 64]
BuiltInDataTypesShadow.Enum16 ---> [varchar] [MaxLength = 255]
BuiltInDataTypesShadow.Enum32 ---> [varchar] [MaxLength = 255]
BuiltInDataTypesShadow.Enum64 ---> [varchar] [MaxLength = 255]
BuiltInDataTypesShadow.Enum8 ---> [varchar] [MaxLength = 255]
BuiltInDataTypesShadow.EnumS8 ---> [varchar] [MaxLength = 255]
BuiltInDataTypesShadow.EnumU16 ---> [varchar] [MaxLength = 255]
BuiltInDataTypesShadow.EnumU32 ---> [varchar] [MaxLength = 255]
BuiltInDataTypesShadow.EnumU64 ---> [varchar] [MaxLength = 255]
BuiltInDataTypesShadow.Id ---> [varchar] [MaxLength = 64]
BuiltInDataTypesShadow.PartitionId ---> [varchar] [MaxLength = 64]
BuiltInDataTypesShadow.TestBoolean ---> [varchar] [MaxLength = 1]
BuiltInDataTypesShadow.TestByte ---> [varchar] [MaxLength = 64]
BuiltInDataTypesShadow.TestCharacter ---> [varchar] [MaxLength = 1]
BuiltInDataTypesShadow.TestDateTime ---> [varchar] [MaxLength = 48]
BuiltInDataTypesShadow.TestDateTimeOffset ---> [varchar] [MaxLength = 48]
BuiltInDataTypesShadow.TestDecimal ---> [varchar] [MaxLength = 64]
BuiltInDataTypesShadow.TestDouble ---> [varchar] [MaxLength = 64]
BuiltInDataTypesShadow.TestInt16 ---> [varchar] [MaxLength = 64]
BuiltInDataTypesShadow.TestInt32 ---> [varchar] [MaxLength = 64]
BuiltInDataTypesShadow.TestInt64 ---> [varchar] [MaxLength = 64]
BuiltInDataTypesShadow.TestSignedByte ---> [varchar] [MaxLength = 64]
BuiltInDataTypesShadow.TestSingle ---> [varchar] [MaxLength = 64]
BuiltInDataTypesShadow.TestTimeSpan ---> [varchar] [MaxLength = 48]
BuiltInDataTypesShadow.TestUnsignedInt16 ---> [varchar] [MaxLength = 64]
BuiltInDataTypesShadow.TestUnsignedInt32 ---> [varchar] [MaxLength = 64]
BuiltInDataTypesShadow.TestUnsignedInt64 ---> [varchar] [MaxLength = 64]
BuiltInNullableDataTypes.Enum16 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypes.Enum32 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypes.Enum64 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypes.Enum8 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypes.EnumS8 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypes.EnumU16 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypes.EnumU32 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypes.EnumU64 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypes.Id ---> [varchar] [MaxLength = 64]
BuiltInNullableDataTypes.PartitionId ---> [varchar] [MaxLength = 64]
BuiltInNullableDataTypes.TestByteArray ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypes.TestNullableBoolean ---> [nullable varchar] [MaxLength = 1]
BuiltInNullableDataTypes.TestNullableByte ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypes.TestNullableCharacter ---> [nullable varchar] [MaxLength = 1]
BuiltInNullableDataTypes.TestNullableDateTime ---> [nullable varchar] [MaxLength = 48]
BuiltInNullableDataTypes.TestNullableDateTimeOffset ---> [nullable varchar] [MaxLength = 48]
BuiltInNullableDataTypes.TestNullableDecimal ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypes.TestNullableDouble ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypes.TestNullableInt16 ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypes.TestNullableInt32 ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypes.TestNullableInt64 ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypes.TestNullableSignedByte ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypes.TestNullableSingle ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypes.TestNullableTimeSpan ---> [nullable varchar] [MaxLength = 48]
BuiltInNullableDataTypes.TestNullableUnsignedInt16 ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypes.TestNullableUnsignedInt32 ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypes.TestNullableUnsignedInt64 ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypes.TestString ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.Enum16 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.Enum32 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.Enum64 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.Enum8 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.EnumS8 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.EnumU16 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.EnumU32 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.EnumU64 ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.Id ---> [varchar] [MaxLength = 64]
BuiltInNullableDataTypesShadow.PartitionId ---> [varchar] [MaxLength = 64]
BuiltInNullableDataTypesShadow.TestByteArray ---> [nullable varchar] [MaxLength = 255]
BuiltInNullableDataTypesShadow.TestNullableBoolean ---> [nullable varchar] [MaxLength = 1]
BuiltInNullableDataTypesShadow.TestNullableByte ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypesShadow.TestNullableCharacter ---> [nullable varchar] [MaxLength = 1]
BuiltInNullableDataTypesShadow.TestNullableDateTime ---> [nullable varchar] [MaxLength = 48]
BuiltInNullableDataTypesShadow.TestNullableDateTimeOffset ---> [nullable varchar] [MaxLength = 48]
BuiltInNullableDataTypesShadow.TestNullableDecimal ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypesShadow.TestNullableDouble ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypesShadow.TestNullableInt16 ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypesShadow.TestNullableInt32 ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypesShadow.TestNullableInt64 ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypesShadow.TestNullableSignedByte ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypesShadow.TestNullableSingle ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypesShadow.TestNullableTimeSpan ---> [nullable varchar] [MaxLength = 48]
BuiltInNullableDataTypesShadow.TestNullableUnsignedInt16 ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypesShadow.TestNullableUnsignedInt32 ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypesShadow.TestNullableUnsignedInt64 ---> [nullable varchar] [MaxLength = 64]
BuiltInNullableDataTypesShadow.TestString ---> [nullable varchar] [MaxLength = 255]
DateTimeEnclosure.DateTimeOffset ---> [nullable varchar] [MaxLength = 48]
DateTimeEnclosure.Id ---> [varchar] [MaxLength = 64]
EmailTemplate.Id ---> [varchar] [MaxLength = 36]
EmailTemplate.TemplateType ---> [varchar] [MaxLength = 255]
MaxLengthDataTypes.ByteArray5 ---> [nullable varchar] [MaxLength = 8]
MaxLengthDataTypes.ByteArray9000 ---> [nullable varchar] [MaxLength = 255]
MaxLengthDataTypes.Id ---> [varchar] [MaxLength = 64]
MaxLengthDataTypes.String3 ---> [nullable varchar] [MaxLength = 3]
MaxLengthDataTypes.String9000 ---> [nullable varchar] [MaxLength = 255]
StringEnclosure.Id ---> [varchar] [MaxLength = 64]
StringEnclosure.Value ---> [nullable varchar] [MaxLength = 255]
StringForeignKeyDataType.Id ---> [varchar] [MaxLength = 64]
StringForeignKeyDataType.StringKeyDataTypeId ---> [nullable varchar] [MaxLength = 255]
StringKeyDataType.Id ---> [varchar] [MaxLength = 255]
UnicodeDataTypes.Id ---> [varchar] [MaxLength = 64]
UnicodeDataTypes.StringAnsi ---> [nullable varchar] [MaxLength = 255]
UnicodeDataTypes.StringAnsi3 ---> [nullable varchar] [MaxLength = 3]
UnicodeDataTypes.StringAnsi9000 ---> [nullable varchar] [MaxLength = 255]
UnicodeDataTypes.StringDefault ---> [nullable varchar] [MaxLength = 255]
UnicodeDataTypes.StringUnicode ---> [nullable varchar] [MaxLength = 255]
";

            Assert.Equal(expected, actual, ignoreLineEndingDifferences: true);
        }

        public override void Can_read_back_mapped_enum_from_collection_first_or_default()
        {
            // The query needs to generate TOP 1
        }

        public override void Can_read_back_bool_mapped_as_int_through_navigation()
        {
            // Column is mapped as int rather than string
        }

        public class EverythingIsStringsJetFixture : BuiltInDataTypesFixtureBase
        {
            public override bool StrictEquality => true;

            public override bool SupportsAnsi => true;

            public override bool SupportsUnicodeToAnsiConversion => true;

            public override bool SupportsLargeStringComparisons => true;

            protected override string StoreName { get; } = "EverythingIsStrings";

            protected override ITestStoreFactory TestStoreFactory => JetStringsTestStoreFactory.Instance;

            public override bool SupportsBinaryKeys => true;

            public override bool SupportsDecimalComparisons => true;

            public override DateTime DefaultDateTime => new DateTime();
            public override bool PreservesDateTimeKind { get; }

            public override string ReallyLargeString
            {
                get
                {
                    //Jet max is 255
                    var res = string.Join("", Enumerable.Repeat("testphrase", 25));
                    return res;
                }
            }

            public override int LongStringLength => 255;

            public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
                => base
                    .AddOptions(builder)
                    .ConfigureWarnings(
                        c => c.Log(JetEventId.DecimalTypeDefaultWarning));

            protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
            {
                base.OnModelCreating(modelBuilder, context);

                modelBuilder.Entity<MaxLengthDataTypes>().Property(e => e.ByteArray5).HasMaxLength(8);
            }
        }

        public class JetStringsTestStoreFactory : JetTestStoreFactory
        {
            public static new JetStringsTestStoreFactory Instance { get; } = new JetStringsTestStoreFactory();

            public override IServiceCollection AddProviderServices(IServiceCollection serviceCollection)
                => base.AddProviderServices(
                    serviceCollection.AddSingleton<IRelationalTypeMappingSource, JetStringsTypeMappingSource>());
        }

        public class JetStringsTypeMappingSource : RelationalTypeMappingSource
        {
            private readonly JetStringTypeMapping _fixedLengthUnicodeString
                = new JetStringTypeMapping(unicode: true, fixedLength: true);

            private readonly JetStringTypeMapping _variableLengthUnicodeString
                = new JetStringTypeMapping(unicode: true);

            private readonly JetStringTypeMapping _fixedLengthAnsiString
                = new JetStringTypeMapping(fixedLength: true);

            private readonly JetStringTypeMapping _variableLengthAnsiString
                = new JetStringTypeMapping();

            private readonly Dictionary<string, RelationalTypeMapping> _storeTypeMappings;

            public JetStringsTypeMappingSource(
                TypeMappingSourceDependencies dependencies,
                RelationalTypeMappingSourceDependencies relationalDependencies)
                : base(dependencies, relationalDependencies)
            {
                _storeTypeMappings
                    = new Dictionary<string, RelationalTypeMapping>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "char varying", _variableLengthAnsiString },
                        { "char", _fixedLengthAnsiString },
                        { "character varying", _variableLengthAnsiString },
                        { "character", _fixedLengthAnsiString },
                        { "national char varying", _variableLengthUnicodeString },
                        { "national character varying", _variableLengthUnicodeString },
                        { "national character", _fixedLengthUnicodeString },
                        { "nchar", _fixedLengthUnicodeString },
                        { "ntext", _variableLengthUnicodeString },
                        { "nvarchar", _variableLengthUnicodeString },
                        { "text", _variableLengthAnsiString },
                        { "varchar", _variableLengthAnsiString }
                    };
            }

            protected override RelationalTypeMapping FindMapping(in RelationalTypeMappingInfo mappingInfo)
                => FindRawMapping(mappingInfo)?.Clone(mappingInfo);

            private RelationalTypeMapping FindRawMapping(RelationalTypeMappingInfo mappingInfo)
            {
                var clrType = mappingInfo.ClrType;
                var storeTypeName = mappingInfo.StoreTypeName;
                var storeTypeNameBase = mappingInfo.StoreTypeNameBase;

                if (storeTypeName != null)
                {
                    if (_storeTypeMappings.TryGetValue(storeTypeName, out var mapping)
                        || _storeTypeMappings.TryGetValue(storeTypeNameBase, out mapping))
                    {
                        return clrType == null
                            || mapping.ClrType == clrType
                                ? mapping
                                : null;
                    }
                }

                //Note - 255 is the max short text length in Jet
                if (clrType == typeof(string))
                {
                    var isAnsi = mappingInfo.IsUnicode == false;
                    var isFixedLength = mappingInfo.IsFixedLength == true;
                    var baseName = isAnsi ? "varchar" : "nvarchar";
                    var maxSize = 255;

                    var size = mappingInfo.Size ?? (mappingInfo.IsKeyOrIndex ? isAnsi ? 255 : 255 : null);
                    if (size > maxSize)
                    {
                        size = isFixedLength ? maxSize : (int?)null;
                    }

                    return new JetStringTypeMapping(
                        baseName + "(" + (size == null ? "255" : size.ToString()) + ")",
                        !isAnsi,
                        size,
                        isFixedLength,
                        storeTypePostfix: size == null ? StoreTypePostfix.None : (StoreTypePostfix?)null);
                }

                return null;
            }
        }
    }
}
