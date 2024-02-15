using System;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using System.Data;
using System.Data.Common;
using System.Linq;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetLongTypeMapping : LongTypeMapping
    {
        public JetLongTypeMapping([NotNull] string storeType,
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
            //Needs to be Object. Using BigInt doesn't always work
            //If the argument value is a long and within Int32 range, having it as BigInt in x86 works
            //When running in x64 it fails to convert. Using Object bypasses the conversion

            var setodbctype = parameter.GetType().GetMethods().FirstOrDefault(x => x.Name == "set_OdbcType");
            var setoledbtype = parameter.GetType().GetMethods().FirstOrDefault(x => x.Name == "set_OleDbType");

            if (setodbctype != null)
            {
                setodbctype.Invoke(parameter, new object?[] { 7 });
            }
            else if (setoledbtype != null)
            {
                setoledbtype.Invoke(parameter, new object?[] { 131 });
            }
        }
    }
}