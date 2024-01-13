// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetMemberTranslatorProvider : RelationalMemberTranslatorProvider
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetMemberTranslatorProvider([NotNull] RelationalMemberTranslatorProviderDependencies dependencies, IRelationalTypeMappingSource typeMappingSource)
            : base(dependencies)
        {
            var sqlExpressionFactory = (JetSqlExpressionFactory)dependencies.SqlExpressionFactory;

            AddTranslators(new IMemberTranslator[]
            {
                new JetDateOnlyMemberTranslator(sqlExpressionFactory),
                new JetDateTimeMemberTranslator(sqlExpressionFactory, typeMappingSource),
                new JetStringMemberTranslator(sqlExpressionFactory),
                new JetTimeSpanMemberTranslator(sqlExpressionFactory),
                new JetTimeOnlyMemberTranslator(sqlExpressionFactory)
            });
        }
    }
}
