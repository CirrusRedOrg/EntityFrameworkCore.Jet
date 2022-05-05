// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Infrastructure.Internal;
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
        private readonly QuerySqlGeneratorDependencies _dependencies;
        private readonly JetSqlExpressionFactory _sqlExpressionFactory;
        private readonly ITypeMappingSource _typeMappingSource;
        private readonly IJetOptions _options;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetQuerySqlGeneratorFactory(
            QuerySqlGeneratorDependencies dependencies,
            ISqlExpressionFactory sqlExpressionFactory,
            ITypeMappingSource typeMappingSource,
            IJetOptions options)
        {
            _dependencies = dependencies;
            _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;
            _typeMappingSource = typeMappingSource;
            _options = options;
        }

        public virtual QuerySqlGenerator Create()
            => new JetQuerySqlGenerator(_dependencies, _sqlExpressionFactory, _typeMappingSource, _options);
    }
}
