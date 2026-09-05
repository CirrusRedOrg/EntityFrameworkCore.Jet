using System.Data;

namespace EntityFrameworkCore.LibRed.Storage.Internal
{
    public class LibRedBoolTypeMapping : BoolTypeMapping
    {
        public static new LibRedBoolTypeMapping Default { get; }  = new("smallint");

        public LibRedBoolTypeMapping(
            string storeType,
            DbType? dbType = System.Data.DbType.Boolean)
            : base(storeType, dbType)
        {
        }

        protected LibRedBoolTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new LibRedBoolTypeMapping(parameters);

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            return (bool)value
                ? "TRUE"
                : "FALSE";
        }
    }
}
