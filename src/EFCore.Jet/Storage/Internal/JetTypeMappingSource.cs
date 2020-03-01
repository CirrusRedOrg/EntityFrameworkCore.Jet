// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
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

        private readonly IntTypeMapping _counter = new IntTypeMapping("counter", DbType.Int32);
        
        private readonly ByteTypeMapping _tinyint = new ByteTypeMapping("tinyint", DbType.Byte);
        private readonly ShortTypeMapping _smallint = new ShortTypeMapping("smallint", DbType.Int16);
        private readonly IntTypeMapping _integer = new IntTypeMapping("integer", DbType.Int32);
        private readonly JetDecimalTypeMapping _bigint = new JetDecimalTypeMapping("decimal", DbType.Decimal, precision: 28, scale: 0, StoreTypePostfix.PrecisionAndScale);

        private readonly FloatTypeMapping _single = new JetFloatTypeMapping("single");
        private readonly DoubleTypeMapping _double = new JetDoubleTypeMapping("double");

        private readonly DecimalTypeMapping _decimal = new JetDecimalTypeMapping("decimal", DbType.Decimal, precision: 18, scale: 10, StoreTypePostfix.PrecisionAndScale);
        private readonly DecimalTypeMapping _currency = new JetCurrencyTypeMapping("currency");

        private readonly JetDateTimeTypeMapping _datetime = new JetDateTimeTypeMapping("datetime", dbType: DbType.DateTime);
        private readonly JetDateTimeOffsetTypeMapping _datetimeoffset = new JetDateTimeOffsetTypeMapping("datetime");
        private readonly JetDateTimeTypeMapping _date = new JetDateTimeTypeMapping("datetime", dbType: DbType.Date);
        private readonly TimeSpanTypeMapping _time = new JetTimeSpanTypeMapping("datetime");

        private readonly JetStringTypeMapping _fixedLengthUnicodeString = new JetStringTypeMapping("char", unicode: true);
        private readonly JetStringTypeMapping _variableLengthUnicodeString = new JetStringTypeMapping("varchar", unicode: true);
        private readonly JetStringTypeMapping _unboundedUnicodeString = new JetStringTypeMapping("longchar", unicode: true, storeTypePostfix: StoreTypePostfix.None);

        private readonly GuidTypeMapping _uniqueidentifier = new GuidTypeMapping("uniqueidentifier", DbType.Guid);
        private readonly JetByteArrayTypeMapping _rowversion = new JetByteArrayTypeMapping("varbinary", size: 8);

        private readonly Dictionary<string, RelationalTypeMapping> _storeTypeMappings;
        private readonly Dictionary<Type, RelationalTypeMapping> _clrTypeMappings;
        private readonly HashSet<string> _disallowedMappings;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetTypeMappingSource(
            [NotNull] TypeMappingSourceDependencies dependencies,
            [NotNull] RelationalTypeMappingSourceDependencies relationalDependencies)
            : base(dependencies, relationalDependencies)
        {
            // References:
            // https://docs.microsoft.com/en-us/previous-versions/office/developer/office2000/aa140015(v=office.10)
            // https://docs.microsoft.com/en-us/office/vba/access/concepts/error-codes/comparison-of-data-types
            // https://support.office.com/en-us/article/equivalent-ansi-sql-data-types-7a0a6bef-ef25-45f9-8a9a-3c5f21b5c65d
            // https://sourcedaddy.com/ms-access/sql-data-types.html

            // TODO: Check the types and their mappings against
            //       https://docs.microsoft.com/en-us/previous-versions/office/developer/office2000/aa140015(v=office.10)
            
            _storeTypeMappings
                = new Dictionary<string, RelationalTypeMapping>(StringComparer.OrdinalIgnoreCase)
                {
                    {"binary", _fixedLengthBinary},

                    {"varbinary", _variableLengthBinary},
                    {"binary varying", _variableLengthBinary},
                    {"bit varying", _variableLengthBinary},

                    {"longbinary", _unboundedBinary},
                    {"general", _unboundedBinary},
                    {"image", _unboundedBinary},
                    {"oleobject", _unboundedBinary},

                    {"bit", _bit},
                    {"boolean", _bit},
                    {"logical", _bit},
                    {"logical1", _bit},
                    {"yesno", _bit},

                    {"counter", _counter},
                    {"identity", _counter},
                    {"autoincrement", _counter},
                    
                    {"tinyint", _tinyint},
                    {"byte", _tinyint},
                    {"integer1", _tinyint},

                    {"smallint", _smallint},
                    {"short", _smallint},
                    {"integer2", _smallint},

                    {"integer", _integer},
                    {"long", _integer},
                    {"int", _integer},
                    {"integer4", _integer},

                    {"single", _single},
                    {"real", _single},
                    {"float4", _single},
                    {"ieeesingle", _single},

                    {"double", _double},
                    {"float", _double},
                    {"float8", _double},
                    {"ieeedouble", _double},
                    {"number", _double},
                    
                    {"decimal", _decimal},
                    {"numeric", _decimal},
                    {"dec", _decimal},
                
                    {"currency", _currency},
                    {"money", _currency},

                    {"datetime", _datetime},
                    {"date", _date},
                    {"time", _time},

                    {"char", _fixedLengthUnicodeString},
                    {"alphanumeric", _fixedLengthUnicodeString},
                    {"character", _fixedLengthUnicodeString},
                    {"nchar", _fixedLengthUnicodeString},
                    {"national char", _fixedLengthUnicodeString},
                    {"national character", _fixedLengthUnicodeString},

                    {"varchar", _variableLengthUnicodeString},
                    {"string", _variableLengthUnicodeString},
                    {"char varying", _variableLengthUnicodeString},
                    {"character varying", _variableLengthUnicodeString},
                    {"national char varying", _variableLengthUnicodeString},
                    {"national character varying", _variableLengthUnicodeString},

                    {"longchar", _unboundedUnicodeString},
                    {"longtext", _unboundedUnicodeString},
                    {"memo", _unboundedUnicodeString},
                    {"note", _unboundedUnicodeString},
                    {"ntext", _unboundedUnicodeString},

                    {"uniqueidentifier", _uniqueidentifier},
                    {"guid", _uniqueidentifier},

                    {"timestamp", _rowversion},
                };

            // Note: sbyte, ushort, uint, char, long and ulong type mappings are not supported by Jet.
            // We would need the type conversions feature to allow this to work - see https://github.com/aspnet/EntityFramework/issues/242.
            _clrTypeMappings
                = new Dictionary<Type, RelationalTypeMapping>
                {
                    {typeof(bool), _bool},
                    {typeof(byte), _tinyint},
                    {typeof(short), _smallint},
                    {typeof(int), _integer},
                    {typeof(long), _bigint}, // uses DECIMAL(28,0)
                    {typeof(float), _single},
                    {typeof(double), _double},
                    {typeof(decimal), _decimal}, // CHECK: Is this supported or do we need to use CURRENCY?
                    {typeof(DateTime), _datetime},
                    {typeof(DateTimeOffset), _datetimeoffset},
                    {typeof(TimeSpan), _time},
                    {typeof(Guid), _uniqueidentifier},
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

                if (_storeTypeMappings.TryGetValue(storeTypeName, out var mapping)
                    || _storeTypeMappings.TryGetValue(storeTypeNameBase, out mapping))
                {
                    return clrType == null
                           || mapping.ClrType == clrType
                        ? mapping
                        : null;
                }
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