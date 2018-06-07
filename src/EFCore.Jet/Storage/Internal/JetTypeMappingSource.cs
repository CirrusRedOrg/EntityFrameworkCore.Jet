// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using EntityFrameworkCore.Jet.Properties;
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
        private readonly JetStringTypeMapping _unboundedUnicodeString
            = new JetStringTypeMapping("text");

        private readonly JetStringTypeMapping _unboundedAnsiString
            = new JetStringTypeMapping("text");

        private readonly JetByteArrayTypeMapping _unboundedBinary
            = new JetByteArrayTypeMapping("image");

        private readonly JetByteArrayTypeMapping _rowversion
            = new JetByteArrayTypeMapping("varbinary(8)", dbType: DbType.Binary, size: 8);

        private readonly IntTypeMapping _int = new IntTypeMapping("int", DbType.Int32);

        private readonly LongTypeMapping _long = new LongTypeMapping("int", DbType.Int64);

        private readonly ShortTypeMapping _short = new ShortTypeMapping("smallint", DbType.Int16);

        private readonly ByteTypeMapping _byte = new ByteTypeMapping("byte", DbType.Byte);

        private readonly JetBoolTypeMapping _bool = new JetBoolTypeMapping("smallint");
        // JET bits are not nullable
        //private readonly JetBoolTypeMapping _bool = new JetBoolTypeMapping("bit");

        private readonly JetStringTypeMapping _fixedLengthUnicodeString
            = new JetStringTypeMapping("char");

        private readonly JetStringTypeMapping _variableLengthUnicodeString
            = new JetStringTypeMapping("varchar");

        // Jet does not support ANSI strings
        /*
        private readonly JetStringTypeMapping _fixedLengthAnsiString
            = new JetStringTypeMapping("char", dbType: DbType.AnsiString);

        private readonly JetStringTypeMapping _variableLengthAnsiString
            = new JetStringTypeMapping("varchar", dbType: DbType.AnsiString);
        */

        private readonly JetByteArrayTypeMapping _variableLengthBinary = new JetByteArrayTypeMapping("varbinary");

        private readonly JetByteArrayTypeMapping _fixedLengthBinary = new JetByteArrayTypeMapping("binary");

        private readonly JetDateTimeTypeMapping _date = new JetDateTimeTypeMapping("datetime", dbType: DbType.Date);

        private readonly JetDateTimeTypeMapping _datetime = new JetDateTimeTypeMapping("datetime", dbType: DbType.DateTime);

        // ReSharper disable once UnusedMember.Local
        private readonly JetDateTimeTypeMapping _datetime2 = new JetDateTimeTypeMapping("datetime", dbType: DbType.DateTime2);

        private readonly DoubleTypeMapping _double = new JetDoubleTypeMapping("double"); 

        private readonly JetDateTimeOffsetTypeMapping _datetimeoffset = new JetDateTimeOffsetTypeMapping("datetime");

        private readonly FloatTypeMapping _real = new JetFloatTypeMapping("single"); 

        private readonly GuidTypeMapping _uniqueidentifier = new GuidTypeMapping("guid", DbType.Guid);

        private readonly DecimalTypeMapping _decimal = new DecimalTypeMapping("decimal(18, 2)", DbType.Decimal);

        private readonly TimeSpanTypeMapping _time = new JetTimeSpanTypeMapping("datetime");

        private readonly JetStringTypeMapping _xml = new JetStringTypeMapping("text");

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
            _storeTypeMappings
                = new Dictionary<string, RelationalTypeMapping>(StringComparer.OrdinalIgnoreCase)
                {
                    { "binary", _fixedLengthBinary },
                    { "bit", _bool },
                    { "byte", _byte },
                    { "char", _fixedLengthUnicodeString },
                    { "date", _date },
                    { "datetime", _datetime },
                    { "decimal", _decimal },
                    { "float", _double },
                    { "double", _double },
                    { "image", _variableLengthBinary },
                    { "int", _int },
                    { "guid", _uniqueidentifier },
                    { "money", _decimal },
                    { "numeric", _decimal },
                    { "real", _real },
                    { "single", _real },
                    { "smalldatetime", _datetime },
                    { "smallint", _short },
                    { "smallmoney", _decimal },
                    { "text", _variableLengthUnicodeString },
                    { "time", _time },
                    { "timestamp", _rowversion },
                    { "tinyint", _byte },
                    { "uniqueidentifier", _uniqueidentifier },
                    { "varbinary", _variableLengthBinary },
                    { "varchar", _variableLengthUnicodeString },
                    { "xml", _xml }
                };

            // Note: sbyte, ushort, uint, char and ulong type mappings are not supported by Jet.
            // We would need the type conversions feature to allow this to work - see https://github.com/aspnet/EntityFramework/issues/242.
            _clrTypeMappings
                = new Dictionary<Type, RelationalTypeMapping>
                {
                    { typeof(int), _int },
                    { typeof(long), _long },
                    { typeof(DateTime), _datetime },
                    { typeof(Guid), _uniqueidentifier },
                    { typeof(bool), _bool },
                    { typeof(byte), _byte },
                    { typeof(double), _double },
                    { typeof(DateTimeOffset), _datetimeoffset },
                    { typeof(short), _short },
                    { typeof(float), _real },
                    { typeof(decimal), _decimal },
                    { typeof(TimeSpan), _time }
                };

            // These are disallowed only if specified without any kind of length specified in parenthesis.
            // This is because we don't try to make a new type from this string and any max length value
            // specified in the model, which means use of these strings is almost certainly an error, and
            // if it is not an error, then using, for example, varbinary(1) will work instead.
            _disallowedMappings
                = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "binary varying",
                    "binary",
                    "char varying",
                    "char",
                    "character varying",
                    "character",
                    "national char varying",
                    "national character varying",
                    "national character",
                    "nchar",
                    "nvarchar",
                    "varbinary",
                    "varchar"
                };



        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void ValidateMapping(CoreTypeMapping mapping, IProperty property)
        {
            RelationalTypeMapping relationalMapping = (RelationalTypeMapping) mapping;

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
        => FindRawMapping(mappingInfo)?.Clone(mappingInfo);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        private RelationalTypeMapping FindRawMapping(RelationalTypeMappingInfo mappingInfo)
        {
            var clrType = mappingInfo.ClrType;
            var storeTypeName = mappingInfo.StoreTypeName;
            var storeTypeNameBase = mappingInfo.StoreTypeNameBase;

            clrType = clrType.UnwrapNullableType().UnwrapEnumType();


            if (storeTypeName != null)
            {
                if (clrType == typeof(float)
                    && mappingInfo.Size != null
                    && mappingInfo.Size <= 24
                    && (storeTypeNameBase.Equals("float", StringComparison.OrdinalIgnoreCase)
                        || storeTypeNameBase.Equals("double precision", StringComparison.OrdinalIgnoreCase)))
                {
                    return _real;
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
                    bool isAnsi = mappingInfo.IsUnicode == false;
                    bool isFixedLength = mappingInfo.IsFixedLength == true;
                    int maxSize = 255;

                    int? size = mappingInfo.Size ?? (mappingInfo.IsKeyOrIndex ? (int?)255 : null);
                    if (size > maxSize)
                    {
                        size = isFixedLength ? maxSize : (int?)null;
                    }

                    return size == null
                        ? isAnsi ? _unboundedAnsiString : _unboundedUnicodeString
                        : new JetStringTypeMapping(storeType: isFixedLength ? "char" : "varchar", size: size);
                }

                if (clrType == typeof(byte[]))
                {
                    if (mappingInfo.IsRowVersion == true)
                    {
                        return _rowversion;
                    }

                    var isFixedLength = mappingInfo.IsFixedLength == true;

                    var size = mappingInfo.Size ?? (mappingInfo.IsKeyOrIndex ? (int?)510 : null);
                    if (size > 510)
                    {
                        size = isFixedLength ? 510 : (int?)null;
                    }

                    return size == null
                        ? _unboundedBinary
                        : new JetByteArrayTypeMapping(size: size, storeType: isFixedLength ? "binary": "varbinary");
                }
            }

            return null;
        }
    }
}
