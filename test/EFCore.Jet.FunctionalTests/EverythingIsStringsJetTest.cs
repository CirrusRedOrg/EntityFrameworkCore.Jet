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
using System.Threading.Tasks;

// ReSharper disable InconsistentNaming
#nullable disable
namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class EverythingIsStringsJetTest(EverythingIsStringsJetTest.EverythingIsStringsJetFixture fixture)
        : BuiltInDataTypesTestBase<
            EverythingIsStringsJetTest.EverythingIsStringsJetFixture>(fixture)
    {

        public override Task Can_read_back_mapped_enum_from_collection_first_or_default()
            // The query needs to generate TOP(1)
            => Task.CompletedTask;

        public override Task Can_read_back_bool_mapped_as_int_through_navigation()
            // Column is mapped as int rather than string
            => Task.CompletedTask;

        public override Task Can_compare_enum_to_constant()
            // Column is mapped as int rather than string
            => Task.CompletedTask;

        public override Task Can_compare_enum_to_parameter()
            // Column is mapped as int rather than string
            => Task.CompletedTask;

        public class EverythingIsStringsJetFixture : BuiltInDataTypesFixtureBase
        {
            public override bool StrictEquality => true;

            public override bool SupportsAnsi => false;

            public override bool SupportsUnicodeToAnsiConversion => false;

            public override bool SupportsLargeStringComparisons => true;

            protected override string StoreName { get; } = "EverythingIsStrings";

            protected override ITestStoreFactory TestStoreFactory => JetStringsTestStoreFactory.Instance;

            public override bool SupportsBinaryKeys => true;

            public override bool SupportsDecimalComparisons => true;

            public override DateTime DefaultDateTime => new();
            public override bool PreservesDateTimeKind { get; }

            public override string ReallyLargeString
                => string.Join("", Enumerable.Repeat("testphrase", 25));

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
            public static new JetStringsTestStoreFactory Instance { get; } = new();

            public override IServiceCollection AddProviderServices(IServiceCollection serviceCollection)
                => base.AddProviderServices(
                    serviceCollection.AddSingleton<IRelationalTypeMappingSource, JetStringsTypeMappingSource>());
        }

        public class JetStringsTypeMappingSource : RelationalTypeMappingSource
        {
            private readonly JetStringTypeMapping _fixedLengthUnicodeString = new(unicode: true, fixedLength: true);

            private readonly JetStringTypeMapping _variableLengthUnicodeString = new(unicode: true);

            private readonly JetStringTypeMapping _fixedLengthAnsiString = new(fixedLength: true);

            private readonly JetStringTypeMapping _variableLengthAnsiString = new();

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
                        size = isFixedLength ? maxSize : null;
                    }

                    return new JetStringTypeMapping(
                        baseName + "(" + (size == null ? "255" : size.ToString()) + ")",
                        !isAnsi,
                        size,
                        isFixedLength,
                        storeTypePostfix: size == null ? StoreTypePostfix.None : null);
                }

                return null;
            }
        }
    }
}
