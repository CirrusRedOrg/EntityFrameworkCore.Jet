// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetFloatTypeMapping : FloatTypeMapping
    {
        public JetFloatTypeMapping(
            string storeType)
            : base(storeType, System.Data.DbType.Single)
        {
        }

        protected JetFloatTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetFloatTypeMapping(parameters);

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
            => Convert.ToSingle(value).ToString("G7", CultureInfo.InvariantCulture);
    }
}
