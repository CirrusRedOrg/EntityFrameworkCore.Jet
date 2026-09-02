// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;

namespace EntityFrameworkCore.LibRed.Storage.Internal
{
    public class LibRedFloatTypeMapping : FloatTypeMapping
    {
        public static new LibRedFloatTypeMapping Default { get; } = new("single");
        public LibRedFloatTypeMapping(
            string storeType)
            : base(storeType, System.Data.DbType.Single)
        {
        }

        protected LibRedFloatTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new LibRedFloatTypeMapping(parameters);

        protected override string ProcessStoreType(RelationalTypeMappingParameters parameters, string storeType, string storeTypeNameBase)
        {
            return base.ProcessStoreType(parameters, storeTypeNameBase, storeTypeNameBase);
        }

        /// <summary>
        ///     Generates the SQL representation of a literal value.
        /// </summary>
        /// <param name="value">The literal value.</param>
        /// <returns>
        ///     The generated string.
        /// </returns>
        protected override string GenerateNonNullSqlLiteral(object value)
            // "R" (shortest round-trippable) rather than G9 or a lossy G7 — see LibRedDoubleTypeMapping:
            //  - G9 always emits 9 digits, so 0.1f becomes 0.100000001 and 85.55f becomes 85.5500031.
            //  - G7 is clean for ordinary values but *rounds*, so it doesn't round-trip: -1.23456789f
            //    renders as -1.234568, 1f/3f as 0.3333333, and float.MaxValue as 3.402823E+38 — a
            //    different float, so `WHERE Float = <literal>` silently fails to match. (Unlike double's
            //    G15, G7 rounds the extremes *down*, so it never yields an out-of-range literal.)
            //  - "R" is the shortest string that parses back exactly: 0.1f/85.55f stay clean, and
            //    float.MaxValue renders as 3.4028235E+38.
            => Convert.ToSingle(value).ToString("R", CultureInfo.InvariantCulture);
    }
}
