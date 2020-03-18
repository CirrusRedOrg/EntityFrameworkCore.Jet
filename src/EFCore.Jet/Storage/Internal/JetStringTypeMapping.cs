// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data;
using System.Data.Common;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetStringTypeMapping : StringTypeMapping
    {
        private readonly int _maxSpecificSize;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetStringTypeMapping(
            [CanBeNull] string storeType = null,
            bool unicode = false,
            int? size = null,
            bool fixedLength = false,
            StoreTypePostfix? storeTypePostfix = null)
            : this(
                new RelationalTypeMappingParameters(
                    new CoreTypeMappingParameters(typeof(string)),
                    storeType ?? GetStoreName(fixedLength),
                    storeTypePostfix ?? StoreTypePostfix.Size,
                    (fixedLength
                        ? System.Data.DbType.String
                        : (DbType?) null),
                    unicode,
                    size,
                    fixedLength))
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected JetStringTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
            _maxSpecificSize = CalculateSize(parameters.Size);
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetStringTypeMapping(parameters);

        private static string GetStoreName(bool fixedLength)
            => fixedLength
                ? "char"
                : "varchar";

        private static int CalculateSize(int? size)
        {
            return size.HasValue && size < 255
                ? size.Value
                : 255;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void ConfigureParameter(DbParameter parameter)
        {
            // For strings and byte arrays, set the max length to the size facet if specified, or
            // 255 characters if no size facet specified, if the data will fit so as to avoid query cache
            // fragmentation by setting lots of different Size values otherwise always set to
            // -1 (unbounded) to avoid SQL client size inference.

            var value = parameter.Value;
            var length = (value as string)?.Length ?? (value as byte[])?.Length;

            parameter.Size = value == null ||
                             value == DBNull.Value ||
                             length != null && length <= _maxSpecificSize
                ? _maxSpecificSize
                : -1;
        }

        /// <summary>
        ///     Generates the SQL representation of a literal value.
        /// </summary>
        /// <param name="value">The literal value.</param>
        /// <returns>
        ///     The generated string.
        /// </returns>
        protected override string GenerateNonNullSqlLiteral(object value)
            => EscapeSqlLiteralWithLineBreaks((string) value);

        private string EscapeSqlLiteralWithLineBreaks(string value)
        {
            // BUG: EF Core indents idempotent scripts, which can lead to unexpected values for strings
            //      that contain line breaks.
            //      Tracked by: https://github.com/aspnet/EntityFrameworkCore/issues/15256
            //
            //      Convert line break characters to their CHR() representation as a workaround.

            return $"'{EscapeSqlLiteral(value)}'"
                .Replace("\r", "' & CHR(13) & '")
                .Replace("\n", "' & CHR(10) & '");
        }
    }
}