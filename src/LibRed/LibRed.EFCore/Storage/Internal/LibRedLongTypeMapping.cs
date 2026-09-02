using Microsoft.EntityFrameworkCore.Storage.Json;

namespace EntityFrameworkCore.LibRed.Storage.Internal
{
    public class LibRedLongTypeMapping : LongTypeMapping
    {
        public static new LibRedLongTypeMapping Default { get; } = new("decimal(20, 0)", precision: 20, scale: 0,
            StoreTypePostfix.PrecisionAndScale);

        public LibRedLongTypeMapping(string storeType,
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

        protected LibRedLongTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new LibRedLongTypeMapping(parameters);
    }
}
