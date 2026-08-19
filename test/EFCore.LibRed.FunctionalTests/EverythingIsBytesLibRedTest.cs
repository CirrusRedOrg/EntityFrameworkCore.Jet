// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Storage.Internal;
using System;
using System.Collections.Generic;
using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using EntityFrameworkCore.LibRed.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Linq;
using System.Threading.Tasks;
#nullable disable
// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.LibRed.FunctionalTests
{
    public class EverythingIsBytesLibRedTest(EverythingIsBytesLibRedTest.EverythingIsBytesLibRedFixture fixture)
        : BuiltInDataTypesTestBase<EverythingIsBytesLibRedTest.EverythingIsBytesLibRedFixture>(fixture)
    {
        public override Task Can_read_back_mapped_enum_from_collection_first_or_default()
            // The query needs to generate TOP(1)
            => Task.CompletedTask;

        public override Task Can_read_back_bool_mapped_as_int_through_navigation()
            // Column is mapped as int rather than byte[]
            => Task.CompletedTask;

        public override Task Object_to_string_conversion()
            // Return values are string which byte[] cannot read
            => Task.CompletedTask;

        public override Task Can_compare_enum_to_constant()
            // Column is mapped as int rather than byte[]
            => Task.CompletedTask;

        public override Task Can_compare_enum_to_parameter()
            // Column is mapped as int rather than byte[]
            => Task.CompletedTask;

        public class EverythingIsBytesLibRedFixture : BuiltInDataTypesFixtureBase
        {
            public override bool StrictEquality => true;

            public override bool SupportsAnsi => true;

            public override bool SupportsUnicodeToAnsiConversion => false;

            public override bool SupportsLargeStringComparisons => true;

            protected override string StoreName { get; } = "EverythingIsBytes";

            protected override ITestStoreFactory TestStoreFactory => LibRedBytesTestStoreFactory.Instance;

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

                modelBuilder.Ignore<Animal>();
                modelBuilder.Ignore<AnimalIdentification>();
                modelBuilder.Ignore<AnimalDetails>();
            }
        }

        public class LibRedBytesTestStoreFactory : LibRedTestStoreFactory
        {
            public static new LibRedBytesTestStoreFactory Instance { get; } = new();

            public override IServiceCollection AddProviderServices(IServiceCollection serviceCollection)
                => base.AddProviderServices(
                    serviceCollection.AddSingleton<IRelationalTypeMappingSource, LibRedBytesTypeMappingSource>());
        }

        public class LibRedBytesTypeMappingSource : RelationalTypeMappingSource
        {
            private readonly JetByteArrayTypeMapping _rowversion = new("rowversion", size: 8);

            private readonly JetByteArrayTypeMapping _variableLengthBinary = new();

            private readonly JetByteArrayTypeMapping _fixedLengthBinary = new(fixedLength: true);

            private readonly Dictionary<string, RelationalTypeMapping> _storeTypeMappings;

            public LibRedBytesTypeMappingSource(
                TypeMappingSourceDependencies dependencies,
                RelationalTypeMappingSourceDependencies relationalDependencies)
                : base(dependencies, relationalDependencies)
            {
                _storeTypeMappings
                    = new Dictionary<string, RelationalTypeMapping>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "binary varying", _variableLengthBinary },
                        { "binary", _fixedLengthBinary },
                        { "image", _variableLengthBinary },
                        { "rowversion", _rowversion },
                        { "varbinary", _variableLengthBinary }
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

                if (clrType == typeof(byte[]))
                {
                    if (mappingInfo.IsRowVersion == true)
                    {
                        return _rowversion;
                    }

                    var isFixedLength = mappingInfo.IsFixedLength == true;

                    var size = mappingInfo.Size ?? (mappingInfo.IsKeyOrIndex ? 255 : null);
                    if (size > 510)
                    {
                        size = isFixedLength ? 510 : null;
                    }

                    return new JetByteArrayTypeMapping(
                        "varbinary(" + (size == null ? "510" : size.ToString()) + ")",
                        size,
                        isFixedLength,
                        storeTypePostfix: size == null ? StoreTypePostfix.None : null);
                }

                return null;
            }
        }
    }
}
