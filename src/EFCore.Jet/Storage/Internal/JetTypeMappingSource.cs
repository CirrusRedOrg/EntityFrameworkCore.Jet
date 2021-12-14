// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using EntityFrameworkCore.Jet.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetTypeMappingSource : RelationalTypeMappingSource
    {
        private readonly JetByteArrayTypeMapping _fixedLengthBinary = new JetByteArrayTypeMapping("binary");
        private readonly JetByteArrayTypeMapping _variableLengthBinary = new JetByteArrayTypeMapping("varbinary");
        private readonly JetByteArrayTypeMapping _unboundedBinary = new JetByteArrayTypeMapping("longbinary", storeTypePostfix: StoreTypePostfix.None);

        private readonly JetBoolTypeMapping _bit = new JetBoolTypeMapping("bit"); // JET bits are not nullable
        private readonly JetBoolTypeMapping _bool = new JetBoolTypeMapping("smallint");

        // We just map counter etc. to integer. Whether an integer property/column is actually a counter
        // is determined by the value generation type.
        private readonly IntTypeMapping _counter = new JetIntTypeMapping("integer");
        
        private readonly ByteTypeMapping _byte = new ByteTypeMapping("byte", DbType.Byte); // unsigned, there is no signed byte in Jet
        private readonly ShortTypeMapping _smallint = new ShortTypeMapping("smallint", DbType.Int16);
        private readonly IntTypeMapping _integer = new JetIntTypeMapping("integer");
        // private readonly JetDecimalTypeMapping _bigint = new JetDecimalTypeMapping("decimal", DbType.Decimal, precision: 28, scale: 0, StoreTypePostfix.PrecisionAndScale);

        private readonly JetFloatTypeMapping _single = new JetFloatTypeMapping("single");
        private readonly JetDoubleTypeMapping _double = new JetDoubleTypeMapping("double");

        private readonly JetDecimalTypeMapping _decimal = new JetDecimalTypeMapping("decimal", DbType.Decimal, precision: 18, scale: 10, StoreTypePostfix.PrecisionAndScale);
        private readonly JetCurrencyTypeMapping _currency = new JetCurrencyTypeMapping("currency");

        private readonly JetDateTimeTypeMapping _datetime;
        private readonly JetDateTimeOffsetTypeMapping _datetimeoffset;
        private readonly JetDateTimeTypeMapping _date;
        private readonly JetTimeSpanTypeMapping _time;

        private readonly JetStringTypeMapping _fixedLengthUnicodeString = new JetStringTypeMapping("char", unicode: true);
        private readonly JetStringTypeMapping _variableLengthUnicodeString = new JetStringTypeMapping("varchar", unicode: true);
        private readonly JetStringTypeMapping _unboundedUnicodeString = new JetStringTypeMapping("longchar", unicode: true, storeTypePostfix: StoreTypePostfix.None);

        private readonly GuidTypeMapping _guid = new GuidTypeMapping("uniqueidentifier", DbType.Guid);
        private readonly JetByteArrayTypeMapping _rowversion = new JetByteArrayTypeMapping("varbinary", size: 8);

        private readonly Dictionary<string, RelationalTypeMapping[]> _storeTypeMappings;
        private readonly Dictionary<Type, RelationalTypeMapping> _clrTypeMappings;
        private readonly HashSet<string> _disallowedMappings;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetTypeMappingSource(
            [NotNull] TypeMappingSourceDependencies dependencies,
            [NotNull] RelationalTypeMappingSourceDependencies relationalDependencies,
            [NotNull] IJetOptions options)
            : base(dependencies, relationalDependencies)
        {
            // References:
            // https://docs.microsoft.com/en-us/previous-versions/office/developer/office2000/aa140015(v=office.10)
            // https://docs.microsoft.com/en-us/office/vba/access/concepts/error-codes/comparison-of-data-types
            // https://support.office.com/en-us/article/equivalent-ansi-sql-data-types-7a0a6bef-ef25-45f9-8a9a-3c5f21b5c65d
            // https://sourcedaddy.com/ms-access/sql-data-types.html

            // TODO: Check the types and their mappings against
            //       https://docs.microsoft.com/en-us/previous-versions/office/developer/office2000/aa140015(v=office.10)
            
            _datetime = new JetDateTimeTypeMapping("datetime", options, dbType: DbType.DateTime);
            _datetimeoffset = new JetDateTimeOffsetTypeMapping("datetime", options);
            _date = new JetDateTimeTypeMapping("datetime", options, dbType: DbType.Date);
            _time = new JetTimeSpanTypeMapping("datetime", options);

            _storeTypeMappings
                = new Dictionary<string, RelationalTypeMapping[]>(StringComparer.OrdinalIgnoreCase)
                {
                    {"binary",                     new[] {_fixedLengthBinary}},

                    {"varbinary",                  new[] {_variableLengthBinary}},
                    {"binary varying",             new[] {_variableLengthBinary}},
                    {"bit varying",                new[] {_variableLengthBinary}},

                    {"longbinary",                 new[] {_unboundedBinary}},
                    {"general",                    new[] {_unboundedBinary}},
                    {"image",                      new[] {_unboundedBinary}},
                    {"oleobject",                  new[] {_unboundedBinary}},

                    {"bit",                        new[] {_bit}},
                    {"boolean",                    new[] {_bit}},
                    {"logical",                    new[] {_bit}},
                    {"logical1",                   new[] {_bit}},
                    {"yesno",                      new[] {_bit}},
                    
                    {"counter",                    new[] {_counter}},
                    {"identity",                   new[] {_counter}},
                    {"autoincrement",              new[] {_counter}},
                    
                    {"byte",                       new[] {_byte}},
                    {"tinyint",                    new[] {_byte}},
                    {"integer1",                   new[] {_byte}},

                    {"smallint",                   new[] {_smallint}},
                    {"short",                      new[] {_smallint}},
                    {"integer2",                   new[] {_smallint}},

                    {"integer",                    new[] {_integer}},
                    {"long",                       new[] {_integer}},
                    {"int",                        new[] {_integer}},
                    {"integer4",                   new[] {_integer}},
                    
                    {"single",                     new[] {_single}},
                    {"real",                       new[] {_single}},
                    {"float4",                     new[] {_single}},
                    {"ieeesingle",                 new[] {_single}},

                    {"double",                     new[] {_double}},
                    {"float",                      new[] {_double}},
                    {"float8",                     new[] {_double}},
                    {"ieeedouble",                 new[] {_double}},
                    {"number",                     new[] {_double}},

                    {"decimal",                    new[] {_decimal}},
                    {"numeric",                    new[] {_decimal}},
                    {"dec",                        new[] {_decimal}},

                    {"currency",                   new[] {_currency}},
                    {"money",                      new[] {_currency}},

                    {"datetime",                   new RelationalTypeMapping[] {_datetime, _datetimeoffset}},
                    {"date",                       new[] {_date}},
                    {"time",                       new[] {_time}},

                    {"char",                       new[] {_fixedLengthUnicodeString}},
                    {"alphanumeric",               new[] {_fixedLengthUnicodeString}},
                    {"character",                  new[] {_fixedLengthUnicodeString}},
                    {"nchar",                      new[] {_fixedLengthUnicodeString}},
                    {"national char",              new[] {_fixedLengthUnicodeString}},
                    {"national character",         new[] {_fixedLengthUnicodeString}},

                    {"varchar",                    new[] {_variableLengthUnicodeString}},
                    {"string",                     new[] {_variableLengthUnicodeString}},
                    {"char varying",               new[] {_variableLengthUnicodeString}},
                    {"character varying",          new[] {_variableLengthUnicodeString}},
                    {"national char varying",      new[] {_variableLengthUnicodeString}},
                    {"national character varying", new[] {_variableLengthUnicodeString}},

                    {"longchar",                   new[] {_unboundedUnicodeString}},
                    {"longtext",                   new[] {_unboundedUnicodeString}},
                    {"memo",                       new[] {_unboundedUnicodeString}},
                    {"note",                       new[] {_unboundedUnicodeString}},
                    {"ntext",                      new[] {_unboundedUnicodeString}},

                    {"guid",                       new[] {_guid}},
                    {"uniqueidentifier",           new[] {_guid}},

                    {"timestamp",                  new[] {_rowversion}},
                };

            // Note: sbyte, ushort, uint, char, long and ulong type mappings are not supported by Jet.
            // We would need the type conversions feature to allow this to work - see https://github.com/aspnet/EntityFramework/issues/242.
            _clrTypeMappings
                = new Dictionary<Type, RelationalTypeMapping>
                {
                    {typeof(bool), _bool},
                    {typeof(byte), _byte},
                    {typeof(sbyte), _smallint},
                    {typeof(short), _smallint},
                    {typeof(int), _integer},
                    // {typeof(long), _bigint}, // uses DECIMAL(28,0)
                    {typeof(float), _single},
                    {typeof(double), _double},
                    {typeof(decimal), _decimal}, // CHECK: Is this supported or do we need to use CURRENCY?
                    {typeof(DateTime), _datetime},
                    {typeof(DateTimeOffset), _datetimeoffset},
                    {typeof(TimeSpan), _time},
                    {typeof(Guid), _guid},
                };

            // These are disallowed only if specified without any kind of length specified in parenthesis.
            // This is because we don't try to make a new type from this string and any max length value
            // specified in the model, which means use of these strings is almost certainly an error, and
            // if it is not an error, then using, for example, varbinary(1) will work instead.
            _disallowedMappings
                = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "binary",

                    "varbinary",
                    "image",
                    "binary varying",
                    "bit varying",

                    "char",
                    "alphanumeric",
                    "character",
                    "nchar",
                    "national char",
                    "national character",

                    "varchar",
                    "string",
                    "char varying",
                    "character varying",
                    "national char varying",
                    "national character varying",
                };
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void ValidateMapping(CoreTypeMapping mapping, IProperty property)
        {
            var relationalMapping = mapping as RelationalTypeMapping;

            if (_disallowedMappings.Contains(relationalMapping?.StoreType))
            {
                if (property == null)
                {
                    throw new ArgumentException(JetStrings.UnqualifiedDataType(relationalMapping.StoreType));
                }

                throw new ArgumentException(JetStrings.UnqualifiedDataTypeOnProperty(relationalMapping.StoreType, property.Name));
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override RelationalTypeMapping FindMapping(in RelationalTypeMappingInfo mappingInfo)
            => base.FindMapping(mappingInfo) ?? FindRawMapping(mappingInfo)?.Clone(mappingInfo);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        private RelationalTypeMapping FindRawMapping(RelationalTypeMappingInfo mappingInfo)
        {
            var clrType = mappingInfo.ClrType;
            var storeTypeName = mappingInfo.StoreTypeName;
            var storeTypeNameBase = mappingInfo.StoreTypeNameBase;

            if (storeTypeName != null)
            {
                // If the TEXT store type is used with a size argument like `TEXT(n)`, it is handled as a synonym
                // for `VARCHAR(n)`.
                // If the TEXT store type is used without a size argument like `TEXT`, it is handled as a synonym
                // for `LONGCHAR`. 
                // See "Notes" in: https://support.office.com/en-us/article/equivalent-ansi-sql-data-types-7a0a6bef-ef25-45f9-8a9a-3c5f21b5c65d
                if (storeTypeNameBase.Equals("text", StringComparison.OrdinalIgnoreCase) &&
                    !mappingInfo.IsFixedLength.GetValueOrDefault())
                {
                    return mappingInfo.Size.GetValueOrDefault() > 0
                        ? _variableLengthUnicodeString
                        : _unboundedUnicodeString;
                }

                // First look for the fully qualified store type name.
                if (_storeTypeMappings.TryGetValue(storeTypeName, out var mappings))
                {
                    // We found the user-specified store type.
                    // If no CLR type was provided, we're probably scaffolding from an existing database. Take the first
                    // mapping as the default.
                    // If a CLR type was provided, look for a mapping between the store and CLR types. If none is found,
                    // fail immediately.
                    return clrType == null
                        ? mappings[0]
                        : mappings.FirstOrDefault(m => m.ClrType == clrType);
                }

                // Then look for the base store type name.
                if (_storeTypeMappings.TryGetValue(storeTypeNameBase, out mappings))
                {
                    return clrType == null
                        ? mappings[0]
                            .Clone(in mappingInfo)
                        : mappings.FirstOrDefault(m => m.ClrType == clrType)
                            ?.Clone(in mappingInfo);
                }
                
                // A store type name was provided, but is unknown. This could be a domain (alias) type, in which case
                // we proceed with a CLR type lookup (if the type doesn't exist at all the failure will come later).
            }

            if (clrType != null)
            {
                if (_clrTypeMappings.TryGetValue(clrType, out var mapping))
                {
                    return mapping;
                }

                if (clrType == typeof(string))
                {
                    var isFixedLength = mappingInfo.IsFixedLength == true;

                    const int maxCharColumnSize = 255;
                    const int maxIndexedCharColumnSize = 255;

                    var size = mappingInfo.Size ?? (mappingInfo.IsKeyOrIndex ? (int?) maxIndexedCharColumnSize : null);
                    if (size > maxCharColumnSize)
                    {
                        size = isFixedLength ? maxCharColumnSize : (int?) null;
                    }

                    return size == null
                        ? _unboundedUnicodeString
                        : new JetStringTypeMapping(
                            storeType: isFixedLength
                                ? _fixedLengthUnicodeString.StoreTypeNameBase
                                : _variableLengthUnicodeString.StoreTypeNameBase,
                            size: size,
                            unicode: true);
                }

                if (clrType == typeof(byte[]))
                {
                    if (mappingInfo.IsRowVersion == true)
                    {
                        return _rowversion;
                    }

                    var isFixedLength = mappingInfo.IsFixedLength == true;

                    const int maxBinaryColumnSize = 510;

                    var size = mappingInfo.Size ?? (mappingInfo.IsKeyOrIndex ? (int?) maxBinaryColumnSize : null);
                    if (size > maxBinaryColumnSize)
                    {
                        size = isFixedLength ? maxBinaryColumnSize : (int?) null;
                    }

                    return size == null
                        ? _unboundedBinary
                        : new JetByteArrayTypeMapping(
                            size: size,
                            storeType: isFixedLength
                                ? _fixedLengthBinary.StoreTypeNameBase
                                : _variableLengthBinary.StoreTypeNameBase);
                }
            }

            return null;
        }
    }
}