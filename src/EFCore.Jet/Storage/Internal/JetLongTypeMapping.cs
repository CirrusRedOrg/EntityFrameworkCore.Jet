using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetLongTypeMapping : LongTypeMapping
    {
        public JetLongTypeMapping([NotNull] string storeType)
            : base(storeType, System.Data.DbType.Int64)
        {
        }
        
        protected JetLongTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetLongTypeMapping(parameters);
    }
}