// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace EntityFrameworkCore.Jet.Query.Internal;

public class JetQueryTranslationPreprocessorFactory(
    QueryTranslationPreprocessorDependencies dependencies,
    RelationalQueryTranslationPreprocessorDependencies relationalDependencies)
    : IQueryTranslationPreprocessorFactory
{
    public virtual QueryTranslationPreprocessor Create(QueryCompilationContext queryCompilationContext)
        => new JetQueryTranslationPreprocessor(
            dependencies,
            relationalDependencies,
            (RelationalQueryCompilationContext)queryCompilationContext);
}
