// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Query.Sql.Internal;
using EntityFrameworkCore.LibRed.Infrastructure;
using EntityFrameworkCore.LibRed.Infrastructure.Internal;

namespace EntityFrameworkCore.LibRed.Query.Sql.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class LibRedQuerySqlGeneratorFactory(
        QuerySqlGeneratorDependencies dependencies,
        ITypeMappingSource typeMappingSource,
        ILibRedOptions options) : IQuerySqlGeneratorFactory
    {
        private readonly QuerySqlGeneratorDependencies _dependencies = dependencies;
        private readonly ITypeMappingSource _typeMappingSource = typeMappingSource;
        private readonly ILibRedOptions _options = options;

        public virtual QuerySqlGenerator Create()
            => _options.SqlMode == LibRedSqlMode.Compatible
                ? new JetQuerySqlGenerator(_dependencies, _typeMappingSource)
                : new LibRedQuerySqlGenerator(_dependencies, _typeMappingSource);
    }
}
