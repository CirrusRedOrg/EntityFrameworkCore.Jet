// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDoubleTypeMapping : DoubleTypeMapping
    {
        public JetDoubleTypeMapping(
            string storeType)
            : base(storeType, System.Data.DbType.Double)
        {
        }

        protected JetDoubleTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetDoubleTypeMapping(parameters);
    }
}
