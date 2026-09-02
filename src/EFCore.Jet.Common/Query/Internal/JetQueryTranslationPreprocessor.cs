// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace EntityFrameworkCore.Jet.Query.Internal;

/// <summary>
///     Extends EF Core's query translation preprocessing with Jet-specific optimizations.
///     Currently adds <c>LongCount() &gt; 0 → Any()</c> which mirrors EF Core's built-in
///     <c>Count() &gt; 0 → Any()</c> optimization that is missing for the long variant.
/// </summary>
public class JetQueryTranslationPreprocessor(
    QueryTranslationPreprocessorDependencies dependencies,
    RelationalQueryTranslationPreprocessorDependencies relationalDependencies,
    RelationalQueryCompilationContext queryCompilationContext)
    : RelationalQueryTranslationPreprocessor(dependencies, relationalDependencies, queryCompilationContext)
{
    public override Expression Process(Expression query)
    {
        query = base.Process(query);
        query = new LongCountToAnyVisitor().Visit(query);
        return query;
    }

    private sealed class LongCountToAnyVisitor : ExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression binaryExpression)
        {
            var result = base.VisitBinary(binaryExpression);
            if (result is not BinaryExpression { NodeType: ExpressionType.GreaterThan } binary)
                return result;

            if (binary.Left is MethodCallExpression { Method.IsGenericMethod: true } longCountCall
                && IsZeroConstant(binary.Right)
                && IsLongCountMethod(longCountCall.Method.GetGenericMethodDefinition()))
            {
                return MakeAnyCall(longCountCall);
            }

            return result;
        }

        private static bool IsZeroConstant(Expression expr)
        {
            if (expr is not ConstantExpression { Value: var val })
                return false;
            return val is int i && i == 0 || val is long l && l == 0;
        }

        private static bool IsLongCountMethod(MethodInfo method)
            => method == QueryableMethods.LongCountWithoutPredicate
                || method == QueryableMethods.LongCountWithPredicate;

        private static Expression MakeAnyCall(MethodCallExpression longCountCall)
        {
            var elementType = longCountCall.Method.GetGenericArguments()[0];
            var anyMethod = longCountCall.Arguments.Count == 2
                ? QueryableMethods.AnyWithPredicate.MakeGenericMethod(elementType)
                : QueryableMethods.AnyWithoutPredicate.MakeGenericMethod(elementType);
            return Expression.Call(anyMethod, longCountCall.Arguments);
        }
    }
}
