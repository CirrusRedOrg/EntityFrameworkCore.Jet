using EntityFrameworkCore.Jet.Query;
using EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal;
using EntityFrameworkCore.LibRed.Infrastructure;
using EntityFrameworkCore.LibRed.Infrastructure.Internal;

namespace EntityFrameworkCore.LibRed.Query.Internal;

/// <summary>
///     This API supports the Entity Framework Core infrastructure and is not intended to be used
///     directly from your code. This API may change or be removed in future releases.
/// </summary>
public class LibRedMethodCallTranslatorProvider : RelationalMethodCallTranslatorProvider
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public LibRedMethodCallTranslatorProvider(
        RelationalMethodCallTranslatorProviderDependencies dependencies,
        ILibRedOptions options)
        : base(dependencies)
    {
        var sqlExpressionFactory = (JetSqlExpressionFactory)dependencies.SqlExpressionFactory;

        // ReSharper disable once VirtualMemberCallInConstructor
        AddTranslators(
            [
                new JetByteArrayMethodTranslator(sqlExpressionFactory),
                new JetConvertTranslator(sqlExpressionFactory),
                new JetDateDiffFunctionsTranslator(sqlExpressionFactory),
                new JetDateOnlyMethodTranslator(sqlExpressionFactory),
                new JetDateTimeMethodTranslator(sqlExpressionFactory),
                new JetIsDateFunctionTranslator(sqlExpressionFactory),
                options.SqlMode == LibRedSqlMode.Compatible
                    ? new JetMathTranslator(sqlExpressionFactory)
                    : new LibRedMathTranslator(sqlExpressionFactory),
                new JetNewGuidTranslator(sqlExpressionFactory),
                new JetObjectToStringTranslator(sqlExpressionFactory),
                new JetParseTranslator(sqlExpressionFactory),
                new JetStringMethodTranslator(sqlExpressionFactory),
                new JetRandomTranslator(sqlExpressionFactory),
                new JetTimeOnlyMethodTranslator(sqlExpressionFactory)
            ]);
    }
}