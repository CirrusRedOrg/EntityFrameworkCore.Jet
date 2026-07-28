// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDoubleTypeMapping : DoubleTypeMapping
    {
        public static new JetDoubleTypeMapping Default { get; } = new("double");

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
            // "R" (shortest round-trippable) rather than the base's "G17" or a lossy "G15":
            //  - G17 always emits 17 digits, so -1.23456789 becomes -1.2345678899999999.
            //  - G15 is clean for ordinary values but *rounds*: 1.0/3.0 drops a digit, and double.MinValue
            //    renders as -1.79769313486232E+308 — a larger magnitude than double.MaxValue, i.e. not a
            //    representable double at all. ACE rejects that literal ("Syntax error in number").
            //  - "R" is the shortest string that parses back exactly: -1.23456789 stays clean, and
            //    double.MinValue renders as -1.7976931348623157E+308, which ACE accepts.
            // ("R" was unreliable on .NET Framework — hence the old G17 advice — but has been
            //  shortest-round-trippable since .NET Core 3.0.)
            var doubleValue = Convert.ToDouble(value);
            var literal = doubleValue.ToString("R", CultureInfo.InvariantCulture);

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
