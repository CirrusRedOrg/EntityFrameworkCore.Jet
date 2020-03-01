using System.Data;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetBoolTypeMapping : BoolTypeMapping
    {
        public JetBoolTypeMapping(
            [NotNull] string storeType,
            DbType? dbType = System.Data.DbType.Boolean)
            : base(storeType, dbType)
        {
        }

        protected JetBoolTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetBoolTypeMapping(parameters);

        protected override string GenerateNonNullSqlLiteral(object value)
            => (bool) value
                ? "True"
                : "False";
    }
}