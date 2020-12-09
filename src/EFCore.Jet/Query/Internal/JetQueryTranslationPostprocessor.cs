// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq.Expressions;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Jet.Query.Internal
{
    public class JetQueryTranslationPostprocessor : RelationalQueryTranslationPostprocessor
    {
        private readonly IJetOptions _options;

        public JetQueryTranslationPostprocessor(
            QueryTranslationPostprocessorDependencies dependencies,
            RelationalQueryTranslationPostprocessorDependencies relationalDependencies,
            QueryCompilationContext queryCompilationContext,
            IJetOptions options)
            : base(dependencies, relationalDependencies, queryCompilationContext)
        {
            _options = options;
        }

        public override Expression Process(Expression query)
        {
            query = base.Process(query);
            
            query = new SearchConditionConvertingExpressionVisitor(SqlExpressionFactory).Visit(query);

            if (_options.EnableMillisecondsSupport)
            {
                query = new JetDateTimeExpressionVisitor(SqlExpressionFactory).Visit(query);
            }

            return query;
        }
    }
}
