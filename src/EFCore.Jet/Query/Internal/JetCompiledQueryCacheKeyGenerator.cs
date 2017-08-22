// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Jet.Query.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetCompiledQueryCacheKeyGenerator : RelationalCompiledQueryCacheKeyGenerator
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetCompiledQueryCacheKeyGenerator(
            [NotNull] CompiledQueryCacheKeyGeneratorDependencies dependencies,
            [NotNull] RelationalCompiledQueryCacheKeyGeneratorDependencies relationalDependencies)
            : base(dependencies, relationalDependencies)
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override object GenerateCacheKey(Expression query, bool async)
        {
            return new JetCompiledQueryCacheKey(
                           GenerateCacheKeyCore(query, async));
        }

        private struct JetCompiledQueryCacheKey
        {
            private readonly RelationalCompiledQueryCacheKey _relationalCompiledQueryCacheKey;

            public JetCompiledQueryCacheKey(
                RelationalCompiledQueryCacheKey relationalCompiledQueryCacheKey)
            {
                _relationalCompiledQueryCacheKey = relationalCompiledQueryCacheKey;
            }

            public override bool Equals(object obj)
                => !ReferenceEquals(null, obj)
                   && obj is JetCompiledQueryCacheKey
                   && Equals((JetCompiledQueryCacheKey)obj);

            private bool Equals(JetCompiledQueryCacheKey other)
                => _relationalCompiledQueryCacheKey.Equals(other._relationalCompiledQueryCacheKey);

            public override int GetHashCode()
            {
                return _relationalCompiledQueryCacheKey.GetHashCode();
            }
        }
    }
}
