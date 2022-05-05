// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetFloatTypeMapping : FloatTypeMapping
    {
        public JetFloatTypeMapping(
            string storeType)
            : base(storeType, System.Data.DbType.Single)
        {
        }

        protected JetFloatTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetFloatTypeMapping(parameters);
    }
}
