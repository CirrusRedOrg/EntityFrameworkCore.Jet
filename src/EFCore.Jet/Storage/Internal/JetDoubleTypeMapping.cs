// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetDoubleTypeMapping : DoubleTypeMapping
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="JetDoubleTypeMapping" /> class.
        /// </summary>
        /// <param name="storeType"> The name of the database type. </param>
        public JetDoubleTypeMapping(
            [NotNull] string storeType)
            : base(storeType, System.Data.DbType.Double)
        {
        }

        /// <summary>
        ///     Creates a copy of this mapping.
        /// </summary>
        /// <param name="storeType"> The name of the database type. </param>
        /// <param name="size"> The size of data the property is configured to store, or null if no size is configured. </param>
        /// <returns> The newly created mapping. </returns>
        public override RelationalTypeMapping Clone(string storeType, int? size)
            => new JetDoubleTypeMapping(storeType);

    }
}
