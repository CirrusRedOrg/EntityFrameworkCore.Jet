// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using EntityFrameworkCore.Jet.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Query.Expressions;
using Microsoft.EntityFrameworkCore.Query.ExpressionTranslators;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetConvertTranslator : IMethodCallTranslator
    {
        private static readonly Dictionary<string, string> _functionName = new Dictionary<string, string>
        {
            [nameof(Convert.ToByte)] = "CByte",
            [nameof(Convert.ToDecimal)] = "CCur", // CDec does not work https://support.microsoft.com/it-it/help/225931/error-message-when-you-use-the-cdec-function-in-an-access-query-the-ex
            [nameof(Convert.ToSingle)] = "CSng",
            [nameof(Convert.ToDouble)] = "CDbl",
            [nameof(Convert.ToInt16)] = "CInt",
            [nameof(Convert.ToInt32)] = "CInt",
            [nameof(Convert.ToInt64)] = "CLng",
            [nameof(Convert.ToString)] = "CStr"
        };

        private static readonly List<Type> _supportedTypes = new List<Type>
        {
            typeof(bool),
            typeof(byte),
            typeof(decimal),
            typeof(double),
            typeof(float),
            typeof(int),
            typeof(long),
            typeof(short),
            typeof(string)
        };

        private static readonly IEnumerable<MethodInfo> _supportedMethods
            = _functionName.Keys
                .SelectMany(
                    t => typeof(Convert).GetTypeInfo().GetDeclaredMethods(t)
                        .Where(
                            m => m.GetParameters().Length == 1
                                 && _supportedTypes.Contains(m.GetParameters().First().ParameterType)));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Expression Translate(MethodCallExpression methodCallExpression)
        {
            return _supportedMethods.Contains(methodCallExpression.Method)
                ?

                new NullCheckedConvertSqlFunctionExpression(
                    _functionName[methodCallExpression.Method.Name],
                    methodCallExpression.Type,
                    methodCallExpression.Arguments[0]
                    )
                : null;
        }
    }
}
