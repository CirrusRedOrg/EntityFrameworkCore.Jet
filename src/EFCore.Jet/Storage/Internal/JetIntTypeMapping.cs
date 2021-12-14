using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetIntTypeMapping : IntTypeMapping
    {
        public JetIntTypeMapping([NotNull] string storeType)
            : base(storeType, System.Data.DbType.Int32)
        {
        }
        
        protected JetIntTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        // JetIntTypeMapping is also used for an explicit counter type, because we actually want it to be integer unless
        // the value generation type is also OnAdd.
        // We therefore lock the store type to its original value (which should be "integer").
        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetIntTypeMapping(parameters.WithStoreTypeAndSize(Parameters.StoreType, parameters.Size));
    }
}