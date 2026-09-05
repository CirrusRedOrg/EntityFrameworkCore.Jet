using Microsoft.EntityFrameworkCore.Storage.Json;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetLongTypeMapping : LongTypeMapping
    {
        public static new JetLongTypeMapping Default { get; } = new("decimal(20, 0)", precision: 20, scale: 0,
            StoreTypePostfix.PrecisionAndScale);

        public JetLongTypeMapping(string storeType,
            int? precision = null,
            int? scale = null,
            StoreTypePostfix storeTypePostfix = StoreTypePostfix.PrecisionAndScale)
            : base(
                new RelationalTypeMappingParameters(
                        new CoreTypeMappingParameters(typeof(Int64), jsonValueReaderWriter: JsonInt64ReaderWriter.Instance),
        storeType,
                        storeTypePostfix,
                        System.Data.DbType.Int64)
                    .WithPrecisionAndScale(precision, scale))
        {
        }

        protected JetLongTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetLongTypeMapping(parameters);

        protected override void ConfigureParameter(DbParameter parameter)
        {
            base.ConfigureParameter(parameter);
            //Needs to be Numeric. Using BigInt doesn't always work
            //If the argument value is a long and within Int32 range, having it as BigInt in x86 works
            //When running in x64 it fails to convert. Using Numeric bypasses the conversion
            //Confirmed since against a real ACE Large Number (BIGINT) column, where it is not "doesn't
            //always" but never: an OleDbType.BigInt parameter carries no value at all, zero included,
            //while Numeric, Decimal and Variant each round-trip the full Int64 range exactly. DBTYPE_I8
            //looks simply never to have been wired into ACE's VARIANT-based coercion - which has been able
            //to hold a decimal since the beginning, hence the type that works being the one for decimals.
            //OdbcType.Numeric = 7;
            //OleDbType.Numeric = 131;

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
    }
}