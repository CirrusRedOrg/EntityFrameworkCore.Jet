using System.Data;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class JetDecimalTypeMapping : DecimalTypeMapping
    {
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public JetDecimalTypeMapping(
            string storeType,
            DbType? dbType = null,
            int? precision = null,
            int? scale = null,
            StoreTypePostfix storeTypePostfix = StoreTypePostfix.PrecisionAndScale)
            : base(
                new RelationalTypeMappingParameters(
                        new CoreTypeMappingParameters(typeof(decimal), jsonValueReaderWriter: JsonDecimalReaderWriter.Instance),
                        storeType,
                        storeTypePostfix,
                        dbType)
                    .WithPrecisionAndScale(precision, scale))
        {
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected JetDecimalTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        {
            var precision = parameters.Precision;
            var scale = parameters.Scale;
            if (parameters.Precision is > 28)
            {
                int prec_diff = parameters.Precision.Value - 28;
                precision = 28;
                if (parameters.Scale is > 28)
                {
                    scale = parameters.Scale.Value - prec_diff;
                }
            }

            if (parameters.StoreType.Contains("bigint"))
            {
                var newparameters = new RelationalTypeMappingParameters(parameters.CoreParameters, "decimal", parameters.StoreTypePostfix,
                    parameters.DbType, parameters.Unicode, parameters.Size, parameters.FixedLength,
                    parameters.Precision, parameters.Scale);
                return new JetDecimalTypeMapping(newparameters.WithPrecisionAndScale(precision, scale));
            }
            return new JetDecimalTypeMapping(parameters.WithPrecisionAndScale(precision, scale));
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override void ConfigureParameter(DbParameter parameter)
        {
            base.ConfigureParameter(parameter);
            //Decimals needs to be mapped to Numeric for Jet.
            //Using Decimal is fine for OleDb but Odbc doesn't like it.
            //Have to use Numeric for Odbc.
            //Suspect this will also fix any formatting erros with , and . for decimal separator and space and , for digit separator
            //OdbcType.Numeric = 7;
            //OleDbType.Numeric = 131;
            if (parameter.DbType != System.Data.DbType.Currency)
            {
                var setodbctype = parameter.GetType().GetMethods().FirstOrDefault(x => x.Name == "set_OdbcType");
                var setoledbtype = parameter.GetType().GetMethods().FirstOrDefault(x => x.Name == "set_OleDbType");

                if (setodbctype != null)
                {
                    setodbctype.Invoke(parameter, [7]);
                }
                else
                {
                    setoledbtype?.Invoke(parameter, [131]);
                }
            }

            if (Size.HasValue
                && Size.Value != -1)
            {
                parameter.Size = Size.Value;
            }

            if (Precision.HasValue)
            {
                parameter.Precision = unchecked((byte)Precision.Value);
            }

            if (Scale.HasValue)
            {
                parameter.Scale = unchecked((byte)Scale.Value);
            }

            if (parameter.Value is decimal dec)
            {
                parameter.Value = decimal.Round(dec, parameter.Scale);
            }
        }

        protected override string GenerateNonNullSqlLiteral(object value)
        {

            if (value is decimal dec && Scale.HasValue)
            {
                return base.GenerateNonNullSqlLiteral(decimal.Round(dec, Scale.Value));
            }
            return base.GenerateNonNullSqlLiteral(value);
        }
    }
}