// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections;
using System.Data;
using System.Text.Json;
using EntityFrameworkCore.Jet.Internal;
using EntityFrameworkCore.LibRed.Infrastructure.Internal;

namespace EntityFrameworkCore.LibRed.Storage.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class LibRedTypeMappingSource : RelationalTypeMappingSource
    {
        private readonly LibRedByteArrayTypeMapping _fixedLengthBinary = new("binary");
        private readonly LibRedByteArrayTypeMapping _variableLengthBinary = new("varbinary");
        private readonly LibRedByteArrayTypeMapping _variableLengthMaxBinary = new("varbinary", storeTypePostfix: StoreTypePostfix.None);
        private readonly LibRedByteArrayTypeMapping _unboundedBinary = new("longbinary", storeTypePostfix: StoreTypePostfix.None);

        private readonly LibRedBoolTypeMapping _bit = new("bit"); // JET bits are not nullable
        private readonly LibRedBoolTypeMapping _bool = LibRedBoolTypeMapping.Default;

        // We just map counter etc. to integer. Whether an integer property/column is actually a counter
        // is determined by the value generation type.
        private readonly LibRedIntTypeMapping _counter = LibRedIntTypeMapping.Default;

        private readonly LibRedByteTypeMapping _byte = LibRedByteTypeMapping.Default; // unsigned, there is no signed byte in Jet
        private readonly LibRedShortTypeMapping _smallint = LibRedShortTypeMapping.Default;
        private readonly LibRedIntTypeMapping _integer = LibRedIntTypeMapping.Default;
        private readonly LibRedLongTypeMapping _bigint = LibRedLongTypeMapping.Default;

        private readonly LibRedFloatTypeMapping _single = LibRedFloatTypeMapping.Default;
        private readonly LibRedDoubleTypeMapping _double = LibRedDoubleTypeMapping.Default;

        private readonly LibRedDecimalTypeMapping _decimal = new("decimal(18,2)", DbType.Decimal, precision: 18, scale: 2, StoreTypePostfix.PrecisionAndScale);
        private readonly LibRedDecimalTypeMapping _decimal18_0 = new("decimal", DbType.Decimal, precision: 18, scale: 0);
        private readonly LibRedDecimalTypeMapping _currency = new("currency", DbType.Currency, storeTypePostfix:StoreTypePostfix.None);

        private readonly LibRedDateTimeTypeMapping _datetime;
        private readonly LibRedDateTimeTypeMapping _dateasdatetime;
        private readonly LibRedDateTimeOffsetTypeMapping _datetimeoffset;
        private readonly LibRedDateOnlyTypeMapping _dateonly;
        private readonly LibRedTimeSpanTypeMapping _timespan;
        private readonly LibRedTimeOnlyTypeMapping _timeonly;

        private readonly LibRedStringTypeMapping _fixedLengthUnicodeString = new("char", unicode: true);
        private readonly LibRedStringTypeMapping _variableLengthUnicodeString = new("varchar", unicode: true);
        private readonly LibRedStringTypeMapping _variableLengthMaxUnicodeString = new("varchar", unicode: true, size: 255, storeTypePostfix: StoreTypePostfix.Size);
        private readonly LibRedStringTypeMapping _unboundedUnicodeString = new("longchar", unicode: true, storeTypePostfix: StoreTypePostfix.None);
        private readonly LibRedGuidTypeMapping _guid;
        private readonly LibRedByteArrayTypeMapping _rowversion = new("varbinary", size: 8,
            comparer: new ValueComparer<byte[]>(
                (v1, v2) => StructuralComparisons.StructuralEqualityComparer.Equals(v1, v2),
                v => StructuralComparisons.StructuralEqualityComparer.GetHashCode(v),
                v => v.ToArray()));

        private readonly Dictionary<string, RelationalTypeMapping[]> _storeTypeMappings;
        private readonly Dictionary<Type, RelationalTypeMapping> _clrTypeMappings;
        private readonly HashSet<string> _disallowedMappings;

        private readonly ILibRedOptions _options;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public LibRedTypeMappingSource(
            TypeMappingSourceDependencies dependencies,
            RelationalTypeMappingSourceDependencies relationalDependencies,
            ILibRedOptions options)
            : base(dependencies, relationalDependencies)
        {
            // References:
            // https://docs.microsoft.com/en-us/previous-versions/office/developer/office2000/aa140015(v=office.10)
            // https://docs.microsoft.com/en-us/office/vba/access/concepts/error-codes/comparison-of-data-types
            // https://support.office.com/en-us/article/equivalent-ansi-sql-data-types-7a0a6bef-ef25-45f9-8a9a-3c5f21b5c65d
            // https://sourcedaddy.com/ms-access/sql-data-types.html

            // LibRed has a single native engine rather than an ODBC/OLE DB driver choice, so there is only
            // one GUID mapping - EFCore.Jet's Odbc variant exists solely for that driver difference.
            _guid = LibRedGuidTypeMapping.Default;

            // TODO: Check the types and their mappings against
            //       https://docs.microsoft.com/en-us/previous-versions/office/developer/office2000/aa140015(v=office.10)

            _datetime = LibRedDateTimeTypeMapping.Default;
            _dateasdatetime = new LibRedDateTimeTypeMapping("date", dbType: DbType.Date);
            _datetimeoffset = LibRedDateTimeOffsetTypeMapping.Default;
            _dateonly = LibRedDateOnlyTypeMapping.Default;
            _timeonly = LibRedTimeOnlyTypeMapping.Default;
            _timespan = LibRedTimeSpanTypeMapping.Default;

            _storeTypeMappings
                = new Dictionary<string, RelationalTypeMapping[]>(StringComparer.OrdinalIgnoreCase)
                {
                    {"binary", [_fixedLengthBinary] },

                    {"varbinary", [_variableLengthBinary] },
                    {"binary varying", [_variableLengthBinary] },
                    {"bit varying", [_variableLengthBinary] },

                    {"longbinary", [_unboundedBinary] },
                    {"general", [_unboundedBinary] },
                    {"image", [_unboundedBinary] },
                    {"oleobject", [_unboundedBinary] },

                    {"bit", [_bit] },
                    {"boolean", [_bit] },
                    {"logical", [_bit] },
                    {"logical1", [_bit] },
                    {"yesno", [_bit] },

                    {"counter", [_counter] },
                    {"identity", [_counter] },
                    {"autoincrement", [_counter] },

                    {"byte", [_byte] },
                    {"tinyint", [_byte] },
                    {"integer1", [_byte] },

                    {"smallint", [_smallint] },
                    {"short", [_smallint] },
                    {"integer2", [_smallint] },

                    {"integer", [_integer] },
                    {"long", [_integer] },
                    {"int", [_integer] },
                    {"integer4", [_integer] },
                    //{"bigint", [_bigint] },

                    {"single", [_single] },
                    {"real", [_single] },
                    {"float4", [_single] },
                    {"ieeesingle", [_single] },

                    {"double", [_double] },
                    {"double precision", [_double] },
                    {"float", [_double] },
                    {"float8", [_double] },
                    {"ieeedouble", [_double] },
                    {"number", [_double] },

                    {"decimal", [_decimal18_0] },
                    {"numeric", [_decimal18_0] },
                    {"dec", [_decimal18_0] },

                    {"currency", [_currency] },
                    {"money", [_currency] },

                    {"datetime", [_datetime] },
                    {"date", [_dateonly, _dateasdatetime] },
                    {"time", [_timeonly, _timespan] },

                    {"char", [_fixedLengthUnicodeString] },
                    {"alphanumeric", [_fixedLengthUnicodeString] },
                    {"character", [_fixedLengthUnicodeString] },
                    {"nchar", [_fixedLengthUnicodeString] },
                    {"national char", [_fixedLengthUnicodeString] },
                    {"national character", [_fixedLengthUnicodeString] },

                    {"varchar", [_variableLengthUnicodeString] },
                    {"string", [_variableLengthUnicodeString] },
                    {"char varying", [_variableLengthUnicodeString] },
                    {"character varying", [_variableLengthUnicodeString] },
                    {"national char varying", [_variableLengthUnicodeString] },
                    {"national character varying", [_variableLengthUnicodeString] },

                    {"longchar", [_unboundedUnicodeString] },
                    {"longtext", [_unboundedUnicodeString] },
                    {"memo", [_unboundedUnicodeString] },
                    {"note", [_unboundedUnicodeString] },
                    {"ntext", [_unboundedUnicodeString] },

                    {"guid", [_guid] },
                    {"uniqueidentifier", [_guid] },

                    {"timestamp", [_rowversion] },
                };

            _clrTypeMappings
                = new Dictionary<Type, RelationalTypeMapping>
                {
                    {typeof(bool), _bool},
                    {typeof(byte), _byte},
                    {typeof(short), _smallint},
                    {typeof(int), _integer},
                    {typeof(long), _bigint},
                    {typeof(float), _single},
                    {typeof(double), _double},
                    {typeof(decimal), _decimal}, // CHECK: Is this supported or do we need to use CURRENCY?
                    {typeof(DateTime), _datetime},
                    {typeof(DateOnly), _dateonly},
                    {typeof(DateTimeOffset), _datetimeoffset},
                    {typeof(TimeSpan), _timespan},
                    {typeof(TimeOnly), _timeonly},
                    {typeof(Guid), _guid},
                    { typeof(JsonTypePlaceholder), LibRedJsonTypeMapping.Default }
                };

            // These are disallowed only if specified without any kind of length specified in parenthesis.
            // This is because we don't try to make a new type from this string and any max length value
            // specified in the model, which means use of these strings is almost certainly an error, and
            // if it is not an error, then using, for example, varbinary(1) will work instead.
            _disallowedMappings
                = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                };

            _options = options;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void ValidateMapping(CoreTypeMapping? mapping, IProperty? property)
        {
            if (mapping is RelationalTypeMapping relationalMapping)
            {
                if (_disallowedMappings.Contains(relationalMapping.StoreType))
                {
                    if (property == null)
                    {
                        throw new ArgumentException(JetStrings.UnqualifiedDataType(relationalMapping.StoreType));
                    }

                    throw new ArgumentException(
                        JetStrings.UnqualifiedDataTypeOnProperty(relationalMapping.StoreType, property.Name));
                }
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
            => base.FindMapping(mappingInfo)
               ?? FindRawMapping(mappingInfo)?.WithTypeMappingInfo(mappingInfo);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        private RelationalTypeMapping? FindRawMapping(RelationalTypeMappingInfo mappingInfo)
        {
            var clrType = mappingInfo.ClrType;
            var storeTypeName = mappingInfo.StoreTypeName;

            // A structural type (complex type or owned entity) mapped to JSON. EF asks for JsonTypePlaceholder
            // *together with* the column's store type, and reports the column as unsupported if the answer is
            // null. Jet stores JSON in a memo, so that store type is longchar - which is also the ordinary
            // unbounded string mapping, whose CLR type is string. The store-type branch below would therefore
            // find longchar, fail to match the placeholder, and answer null, so the question has to be settled
            // before the store type gets a chance to shadow it.
            if (clrType == typeof(JsonTypePlaceholder))
            {
                return mappingInfo.StoreTypeNameBase switch
                {
                    "longchar" => LibRedJsonTypeMapping.Default,

                    // TEXT without a size is a synonym for LONGCHAR, as above.
                    "text" when !mappingInfo.Size.HasValue => LibRedJsonTypeMapping.Default,

                    // Sized, so it gets cloned outside for the right size.
                    "varchar" or "text" => LibRedJsonTypeMapping.VarcharDefault,

                    null => LibRedJsonTypeMapping.Default,

                    // Anything else genuinely cannot hold JSON, and null is what tells EF to say so.
                    _ => null
                };
            }

            if (storeTypeName != null)
            {
                var storeTypeNameBase = mappingInfo.StoreTypeNameBase;
                // If the TEXT store type is used with a size argument like `TEXT(n)`, it is handled as a synonym
                // for `VARCHAR(n)`.
                // If the TEXT store type is used without a size argument like `TEXT`, it is handled as a synonym
                // for `LONGCHAR`.
                // See "Notes" in: https://support.office.com/en-us/article/equivalent-ansi-sql-data-types-7a0a6bef-ef25-45f9-8a9a-3c5f21b5c65d
                if (storeTypeNameBase!.Equals("text", StringComparison.OrdinalIgnoreCase) &&
                    !mappingInfo.IsFixedLength.GetValueOrDefault())
                {
                    if (mappingInfo.Size.GetValueOrDefault() > 0)
                    {
                        return clrType == null
                               || _variableLengthUnicodeString.ClrType == clrType
                            ? _variableLengthUnicodeString
                            : null;
                    }
                    else
                    {
                        return clrType == null
                               || _unboundedUnicodeString.ClrType == clrType
                            ? _unboundedUnicodeString
                            : null;
                    }
                }

                if (_storeTypeMappings.TryGetValue(storeTypeName, out var mappings)
                    || _storeTypeMappings.TryGetValue(storeTypeNameBase, out mappings))
                {
                    // We found the user-specified store type. No CLR type was provided - we're probably scaffolding from an existing database,
                    // take the first mapping as the default.
                    if (clrType is null)
                    {
                        return mappings[0];
                    }

                    // A CLR type was provided - look for a mapping between the store and CLR types. If not found, fail
                    // immediately.
                    foreach (var m in mappings)
                    {
                        if (m.ClrType == clrType)
                        {
                            return m;
                        }
                    }

                    return null;
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

                    var size = mappingInfo.Size ?? (mappingInfo.IsKeyOrIndex ? maxIndexedCharColumnSize : null);
                    bool extendtolongchar = false;
                    if (size > maxCharColumnSize)
                    {
                        size = isFixedLength ? maxCharColumnSize : null;
                        extendtolongchar = true;
                    }

                    if (_options.UseShortTextForSystemString && size == null && !extendtolongchar)

                    {
                        return new LibRedStringTypeMapping("varchar", unicode: true, size: 255);
                    }

                    return size == null
                        ? _unboundedUnicodeString
                        : new LibRedStringTypeMapping(
                            storeType: isFixedLength
                                ? _fixedLengthUnicodeString.StoreTypeNameBase
                                : _variableLengthUnicodeString.StoreTypeNameBase,
                            size: size,
                            unicode: true,
                            useKeyComparison: mappingInfo.IsKeyOrIndex);
                }

                if (clrType == typeof(byte[]))
                {
                    if (mappingInfo.IsRowVersion == true)
                    {
                        return _rowversion;
                    }

                    if (mappingInfo.ElementTypeMapping == null)
                    {
                        var isFixedLength = mappingInfo.IsFixedLength == true;

                        const int maxBinaryColumnSize = 510;

                        var size = mappingInfo.Size ?? (mappingInfo.IsKeyOrIndex ? maxBinaryColumnSize : null);
                        if (size > maxBinaryColumnSize)
                        {
                            size = isFixedLength ? maxBinaryColumnSize : null;
                        }

                        return size == null
                            ? _unboundedBinary
                            : new LibRedByteArrayTypeMapping(
                                size: size,
                                storeType: isFixedLength
                                    ? _fixedLengthBinary.StoreTypeNameBase
                                    : _variableLengthBinary.StoreTypeNameBase);
                    }
                }
            }

            return null;
        }

        private static readonly List<string> NameBasesUsingPrecision =
        [
            "decimal",
            "dec",
            "numeric"
        ];

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override string? ParseStoreTypeName(
            string? storeTypeName,
            ref bool? unicode,
            ref int? size,
            ref int? precision,
            ref int? scale)
        {
            if (storeTypeName == null)
            {
                return null;
            }

            var originalSize = size;
            var parsedName = base.ParseStoreTypeName(storeTypeName, ref unicode, ref size, ref precision, ref scale);

            if (size.HasValue
                && NameBasesUsingPrecision.Any(n => storeTypeName.StartsWith(n, StringComparison.OrdinalIgnoreCase)))
            {
                precision = size;
                size = originalSize;
            }

            return parsedName;
        }
    }
}
