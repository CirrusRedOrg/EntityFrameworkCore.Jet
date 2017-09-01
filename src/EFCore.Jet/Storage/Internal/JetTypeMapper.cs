// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using EntityFrameworkCore.Jet.Properties;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using EntityFrameworkCore.Jet.Utilities;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetTypeMapper : RelationalTypeMapper
    {
        private readonly JetStringTypeMapping _unboundedUnicodeString
            = new JetStringTypeMapping("text", dbType: null, unicode: true);

        private readonly JetStringTypeMapping _keyUnicodeString
            = new JetStringTypeMapping("varchar(255)", dbType: null, unicode: true, size: 255);

        private readonly JetStringTypeMapping _unboundedAnsiString
            = new JetStringTypeMapping("text", dbType: DbType.AnsiString);

        private readonly JetStringTypeMapping _keyAnsiString
            = new JetStringTypeMapping("varchar(255)", dbType: DbType.AnsiString, unicode: false, size: 255);

        private readonly JetByteArrayTypeMapping _unboundedBinary
            = new JetByteArrayTypeMapping("image");

        private readonly JetByteArrayTypeMapping _keyBinary
            = new JetByteArrayTypeMapping("varbinary(255)", dbType: DbType.Binary, size: 255);

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
            = new JetStringTypeMapping("char", dbType: DbType.String, unicode: true);

        private readonly JetStringTypeMapping _variableLengthUnicodeString
            = new JetStringTypeMapping("varchar", dbType: null, unicode: true);

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

        private readonly JetDateTimeTypeMapping _datetime2 = new JetDateTimeTypeMapping("datetime", dbType: DbType.DateTime2);

        private readonly DoubleTypeMapping _double = new JetDoubleTypeMapping("float"); 

        private readonly JetDateTimeOffsetTypeMapping _datetimeoffset = new JetDateTimeOffsetTypeMapping("datetime");

        private readonly FloatTypeMapping _real = new JetFloatTypeMapping("real"); 

        private readonly GuidTypeMapping _uniqueidentifier = new GuidTypeMapping("guid", DbType.Guid);

        private readonly DecimalTypeMapping _decimal = new DecimalTypeMapping("decimal(18, 2)");

        private readonly TimeSpanTypeMapping _time = new JetTimeSpanTypeMapping("datetime");

        private readonly JetStringTypeMapping _xml = new JetStringTypeMapping("text", dbType: null, unicode: true);

        private readonly Dictionary<string, RelationalTypeMapping> _storeTypeMappings;
        private readonly Dictionary<Type, RelationalTypeMapping> _clrTypeMappings;
        private readonly HashSet<string> _disallowedMappings;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetTypeMapper([NotNull] RelationalTypeMapperDependencies dependencies)
            : base(dependencies)
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
                    { "image", _variableLengthBinary },
                    { "int", _int },
                    { "guid", _uniqueidentifier },
                    { "money", _decimal },
                    { "numeric", _decimal },
                    { "real", _real },
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

            ByteArrayMapper
                = new ByteArrayRelationalTypeMapper(
                    maxBoundedLength: 510,
                    defaultMapping: _unboundedBinary,
                    unboundedMapping: _unboundedBinary,
                    keyMapping: _keyBinary,
                    rowVersionMapping: _rowversion,
                    createBoundedMapping: size => new JetByteArrayTypeMapping(
                        "varbinary(" + size + ")",
                        DbType.Binary,
                        size));

            StringMapper
                = new StringRelationalTypeMapper(
                    maxBoundedAnsiLength: 255,
                    defaultAnsiMapping: _unboundedAnsiString,
                    unboundedAnsiMapping: _unboundedAnsiString,
                    keyAnsiMapping: _keyAnsiString,
                    createBoundedAnsiMapping: size => new JetStringTypeMapping(
                        "varchar(" + size + ")",
                        DbType.AnsiString,
                        unicode: false,
                        size: size),
                    maxBoundedUnicodeLength: 255,
                    defaultUnicodeMapping: _unboundedUnicodeString,
                    unboundedUnicodeMapping: _unboundedUnicodeString,
                    keyUnicodeMapping: _keyUnicodeString,
                    createBoundedUnicodeMapping: size => new JetStringTypeMapping(
                        "varchar(" + size + ")",
                        dbType: null,
                        unicode: true,
                        size: size));
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override IByteArrayRelationalTypeMapper ByteArrayMapper { get; }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override IStringRelationalTypeMapper StringMapper { get; }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override void ValidateTypeName(string storeType)
        {
            if (_disallowedMappings.Contains(storeType))
            {
                throw new ArgumentException(JetStrings.UnqualifiedDataType(storeType));
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override string GetColumnType(IProperty property) => property.Jet().ColumnType;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override IReadOnlyDictionary<Type, RelationalTypeMapping> GetClrTypeMappings()
            => _clrTypeMappings;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override IReadOnlyDictionary<string, RelationalTypeMapping> GetStoreTypeMappings()
            => _storeTypeMappings;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override RelationalTypeMapping FindMapping(Type clrType)
        {
            Check.NotNull(clrType, nameof(clrType));

            clrType = clrType.UnwrapNullableType().UnwrapEnumType();

            return clrType == typeof(string)
                ? _unboundedUnicodeString
                : (clrType == typeof(byte[])
                    ? _unboundedBinary
                    : base.FindMapping(clrType));
        }

        // Indexes in Jet have a max size of 900 bytes
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override bool RequiresKeyMapping(IProperty property)
            => base.RequiresKeyMapping(property) || property.IsIndex();
    }
}
