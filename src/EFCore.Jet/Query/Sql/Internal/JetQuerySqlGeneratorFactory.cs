// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Query.Sql.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
    {
        [NotNull] private readonly QuerySqlGeneratorDependencies _dependencies;
        [NotNull] private readonly JetSqlExpressionFactory _sqlExpressionFactory;
        [NotNull] private readonly ITypeMappingSource _typeMappingSource;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetQuerySqlGeneratorFactory(
            [NotNull] QuerySqlGeneratorDependencies dependencies,
            [NotNull] ISqlExpressionFactory sqlExpressionFactory,
            [NotNull] ITypeMappingSource typeMappingSource)
        {
            _dependencies = dependencies;
            _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;
            _typeMappingSource = typeMappingSource;
        }

        public virtual QuerySqlGenerator Create()
            => new JetQuerySqlGenerator(_dependencies, _sqlExpressionFactory, _typeMappingSource);
    }
}
