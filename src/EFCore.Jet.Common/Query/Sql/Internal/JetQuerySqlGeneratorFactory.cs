namespace EntityFrameworkCore.Jet.Query.Sql.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    /// <remarks>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </remarks>
    public class JetQuerySqlGeneratorFactory(
        QuerySqlGeneratorDependencies dependencies,
        ITypeMappingSource typeMappingSource) : IQuerySqlGeneratorFactory
    {
        private readonly QuerySqlGeneratorDependencies _dependencies = dependencies;
        private readonly ITypeMappingSource _typeMappingSource = typeMappingSource;

        public virtual QuerySqlGenerator Create()
            => new JetQuerySqlGenerator(_dependencies, _typeMappingSource);
    }
}
