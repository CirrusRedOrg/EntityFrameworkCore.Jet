// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetByteArrayTypeMapping : ByteArrayTypeMapping
    {
        public static new JetByteArrayTypeMapping Default { get; } = new();
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetByteArrayTypeMapping(
            string? storeType = null,
            int? size = null,
            bool fixedLength = false,
            ValueComparer? comparer = null,
            StoreTypePostfix? storeTypePostfix = null)
            : base(
                new RelationalTypeMappingParameters(
                    new CoreTypeMappingParameters(typeof(byte[]), null, comparer, jsonValueReaderWriter: JsonByteArrayReaderWriter.Instance),
                    storeType ?? (fixedLength ? "binary" : "varbinary"),
                    storeTypePostfix ?? StoreTypePostfix.Size,
                    System.Data.DbType.Binary,
                    size: size,
                    fixedLength: fixedLength))
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected JetByteArrayTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetByteArrayTypeMapping(parameters);

        private static int CalculateSize(int? size)
            => size.HasValue && size < 510 ? size.Value : 510;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void ConfigureParameter(DbParameter parameter)
        {
            // For strings and byte arrays, set the max length to the size facet if specified, or
            // 8000 bytes if no size facet specified, if the data will fit so as to avoid query cache
            // fragmentation by setting lots of different Size values otherwise always set to 
            // -1 (unbounded) to avoid SQL client size inference.

            var value = parameter.Value;
            var length = (value as string)?.Length ?? (value as byte[])?.Length;
            int maxSpecificSize = CalculateSize(Size);

            parameter.Size = value == null || value == DBNull.Value || length != null && length <= maxSpecificSize
                ? maxSpecificSize
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
        {
            var bytes = (byte[])value;

            // An empty array has no hex form here. SQL Server writes a bare '0x', but that is T-SQL only —
            // ACE rejects it outright ("Syntax error in query expression '0x'"), so the SQL was invalid for
            // this provider rather than merely unusual, and failed the same way on ACE and on LibRed.
            //
            // An empty STRING literal is what Access accepts, in a value position and in a DEFAULT. Verified
            // against ACE: '' stores and reads back as a zero-length byte[], and stays distinct from NULL
            // (IS NULL does not match it). Note 0x00 is NOT the same thing — that is a one-byte zero.
            if (bytes.Length == 0)
            {
                return "''";
            }

            var builder = new StringBuilder(2 + bytes.Length * 2);
            builder.Append("0x");

            foreach (var @byte in bytes)
            {
                builder.Append(@byte.ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        protected override string ProcessStoreType(RelationalTypeMappingParameters parameters, string storeType, string storeTypeNameBase)
        {
            return base.ProcessStoreType(parameters, storeType.Replace("(max)", ""), storeTypeNameBase);
        }
    }
}
