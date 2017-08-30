// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query.ExpressionTranslators;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetCompositeMethodCallTranslator : RelationalCompositeMethodCallTranslator
    {
        private static readonly IMethodCallTranslator[] _methodCallTranslators =
        {
            new JetContainsOptimizedTranslator(),
            new JetConvertTranslator(),
            new JetDateAddTranslator(),
            new JetEndsWithTranslator(),
            new JetMathTranslator(),
            new JetNewGuidTranslator(),
            new JetObjectToStringTranslator(),
            new JetStartsWithTranslator(),
            new JetStringIsNullOrWhiteSpaceTranslator(),
            new JetStringReplaceTranslator(),
            new JetStringSubstringTranslator(),
            new JetStringToLowerTranslator(),
            new JetStringToUpperTranslator(),
            new JetStringTrimEndTranslator(),
            new JetStringTrimStartTranslator(),
            new JetStringTrimTranslator()
        };

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetCompositeMethodCallTranslator(
            [NotNull] RelationalCompositeMethodCallTranslatorDependencies dependencies)
            : base(dependencies)
        {
            // ReSharper disable once DoNotCallOverridableMethodsInConstructor
            AddTranslators(_methodCallTranslators);
        }
    }
}
