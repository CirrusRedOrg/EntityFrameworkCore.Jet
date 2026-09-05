using System.Data;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace EntityFrameworkCore.LibRed.Storage.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class LibRedDecimalTypeMapping : DecimalTypeMapping
    {

        public static new LibRedDecimalTypeMapping Default { get; } = new("decimal(18,2)", System.Data.DbType.Decimal, precision: 18, scale: 2, StoreTypePostfix.PrecisionAndScale);
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public LibRedDecimalTypeMapping(
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
        protected LibRedDecimalTypeMapping(RelationalTypeMappingParameters parameters)
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
                return new LibRedDecimalTypeMapping(newparameters.WithPrecisionAndScale(precision, scale));
            }
            return new LibRedDecimalTypeMapping(parameters.WithPrecisionAndScale(precision, scale));
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
