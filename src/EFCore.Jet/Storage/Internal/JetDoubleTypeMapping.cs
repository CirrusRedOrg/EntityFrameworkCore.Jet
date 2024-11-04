// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDoubleTypeMapping : DoubleTypeMapping
    {
        public JetDoubleTypeMapping(
            string storeType)
            : base(storeType, System.Data.DbType.Double)
        {
        }

        protected JetDoubleTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetDoubleTypeMapping(parameters);

        protected override string ProcessStoreType(RelationalTypeMappingParameters parameters, string storeType, string storeTypeNameBase)
        {
            return base.ProcessStoreType(parameters, storeTypeNameBase, storeTypeNameBase);
        }

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            //The base can have some rounding problems
            //-1.23456789 can have multiple 9s at the end
            //Base uses format of G17
            var doubleValue = Convert.ToDouble(value);
            var literal = doubleValue.ToString("G15", CultureInfo.InvariantCulture);

            return !literal.Contains('E')
                   && !literal.Contains('e')
                   && !literal.Contains('.')
                   && !double.IsNaN(doubleValue)
                   && !double.IsInfinity(doubleValue)
                ? literal + ".0"
                : literal;
        }
    }
}
