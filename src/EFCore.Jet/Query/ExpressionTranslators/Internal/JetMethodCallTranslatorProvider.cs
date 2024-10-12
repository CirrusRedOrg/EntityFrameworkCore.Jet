// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetMethodCallTranslatorProvider : RelationalMethodCallTranslatorProvider
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetMethodCallTranslatorProvider(
            RelationalMethodCallTranslatorProviderDependencies dependencies)
            : base(dependencies)
        {
            var sqlExpressionFactory = (JetSqlExpressionFactory)dependencies.SqlExpressionFactory;

            // ReSharper disable once VirtualMemberCallInConstructor
            AddTranslators(
                new IMethodCallTranslator[]
                {
                    new JetByteArrayMethodTranslator(sqlExpressionFactory),
                    new JetConvertTranslator(sqlExpressionFactory),
                    new JetDateDiffFunctionsTranslator(sqlExpressionFactory),
                    new JetDateOnlyMethodTranslator(sqlExpressionFactory),
                    new JetDateTimeMethodTranslator(sqlExpressionFactory),
                    new JetIsDateFunctionTranslator(sqlExpressionFactory),
                    new JetMathTranslator(sqlExpressionFactory),
                    new JetNewGuidTranslator(sqlExpressionFactory),
                    new JetObjectToStringTranslator(sqlExpressionFactory),
                    new JetStringMethodTranslator(sqlExpressionFactory),
                    new JetRandomTranslator(sqlExpressionFactory),
                    new JetTimeOnlyMethodTranslator(sqlExpressionFactory)
                });
        }
    }
}