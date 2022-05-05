// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data.Common;
using JetBrains.Annotations;

namespace EntityFrameworkCore.Jet.Scaffolding.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public static class JetDataReaderExtension
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public static T? GetValueOrDefault<T>(this DbDataReader reader, string name, T? defaultValue = default)
        {
            var idx = reader.GetOrdinal(name);
            return reader.IsDBNull(idx)
                ? defaultValue
                : reader.GetFieldValue<T>(idx);
        }
    }
}
