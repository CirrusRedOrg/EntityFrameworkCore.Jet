// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetConvertTranslator : IMethodCallTranslator
    {
        private readonly JetSqlExpressionFactory _sqlExpressionFactory;

        // The value here is actually never used.
        private static readonly Dictionary<string, string> _functionName = new Dictionary<string, string>
        {
            [nameof(Convert.ToBoolean)] = "CBOOL",
            [nameof(Convert.ToByte)] = "CBYTE",
            [nameof(Convert.ToInt16)] = "CINT",
            [nameof(Convert.ToInt32)] = "CLNG",
            // [nameof(Convert.ToInt64)] = "CDEC", // CDEC does not work https://docs.microsoft.com/en-us/office/troubleshoot/access/cdec-function-error
            [nameof(Convert.ToDecimal)] = "CCUR", // CDEC does not work https://docs.microsoft.com/en-us/office/troubleshoot/access/cdec-function-error
            [nameof(Convert.ToSingle)] = "CSNG",
            [nameof(Convert.ToDouble)] = "CDBL",
            [nameof(Convert.ToDateTime)] = "CDATE",
            [nameof(Convert.ToString)] = "CSTR",

            [nameof(Boolean.Parse)] = "CBOOL",
            [nameof(Byte.Parse)] = "CBYTE",
            [nameof(Int16.Parse)] = "CINT",
            [nameof(Int32.Parse)] = "CLNG",
            // [nameof(Convert.ToInt64)] = "CDEC", // CDEC does not work https://docs.microsoft.com/en-us/office/troubleshoot/access/cdec-function-error
            [nameof(Decimal.Parse)] = "CCUR", // CDEC does not work https://docs.microsoft.com/en-us/office/troubleshoot/access/cdec-function-error
            [nameof(Single.Parse)] = "CSNG",
            [nameof(Double.Parse)] = "CDBL",
            [nameof(DateTime.Parse)] = "CDATE"
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
                    t => typeof(Convert).GetTypeInfo()
                        .GetDeclaredMethods(t)
                        .Where(
                            m => m.GetParameters()
                                     .Length == 1
                                 && _supportedTypes.Contains(
                                     m.GetParameters()
                                         .First()
                                         .ParameterType)))
                .ToList();


        private static readonly IEnumerable<MethodInfo> _supportedBoolParseMethods
            = _functionName.Keys
                .SelectMany(
                    t => typeof(Boolean).GetTypeInfo()
                        .GetDeclaredMethods(t)
                        .Where(
                            m => m.GetParameters()
                                     .Length == 1
                                 && _supportedTypes.Contains(
                                     m.GetParameters()
                                         .First()
                                         .ParameterType)))
                .ToList();

        private static readonly IEnumerable<MethodInfo> _supportedByteParseMethods
            = _functionName.Keys
                .SelectMany(
                    t => typeof(Byte).GetTypeInfo()
                        .GetDeclaredMethods(t)
                        .Where(
                            m => m.GetParameters()
                                     .Length == 1
                                 && _supportedTypes.Contains(
                                     m.GetParameters()
                                         .First()
                                         .ParameterType)))
                .ToList();

        private static readonly IEnumerable<MethodInfo> _supportedInt16ParseMethods
            = _functionName.Keys
                .SelectMany(
                    t => typeof(Int16).GetTypeInfo()
                        .GetDeclaredMethods(t)
                        .Where(
                            m => m.GetParameters()
                                     .Length == 1
                                 && _supportedTypes.Contains(
                                     m.GetParameters()
                                         .First()
                                         .ParameterType)))
                .ToList();

        private static readonly IEnumerable<MethodInfo> _supportedInt32ParseMethods
            = _functionName.Keys
                .SelectMany(
                    t => typeof(Int32).GetTypeInfo()
                        .GetDeclaredMethods(t)
                        .Where(
                            m => m.GetParameters()
                                     .Length == 1
                                 && _supportedTypes.Contains(
                                     m.GetParameters()
                                         .First()
                                         .ParameterType)))
                .ToList();

        private static readonly IEnumerable<MethodInfo> _supportedDecimalParseMethods
            = _functionName.Keys
                .SelectMany(
                    t => typeof(Decimal).GetTypeInfo()
                        .GetDeclaredMethods(t)
                        .Where(
                            m => m.GetParameters()
                                     .Length == 1
                                 && _supportedTypes.Contains(
                                     m.GetParameters()
                                         .First()
                                         .ParameterType)))
                .ToList();

        private static readonly IEnumerable<MethodInfo> _supportedSingleParseMethods
            = _functionName.Keys
                .SelectMany(
                    t => typeof(Single).GetTypeInfo()
                        .GetDeclaredMethods(t)
                        .Where(
                            m => m.GetParameters()
                                     .Length == 1
                                 && _supportedTypes.Contains(
                                     m.GetParameters()
                                         .First()
                                         .ParameterType)))
                .ToList();

        private static readonly IEnumerable<MethodInfo> _supportedDoubleParseMethods
            = _functionName.Keys
                .SelectMany(
                    t => typeof(Double).GetTypeInfo()
                        .GetDeclaredMethods(t)
                        .Where(
                            m => m.GetParameters()
                                     .Length == 1
                                 && _supportedTypes.Contains(
                                     m.GetParameters()
                                         .First()
                                         .ParameterType)))
                .ToList();

        private static readonly IEnumerable<MethodInfo> _supportedDateTimeParseMethods
            = _functionName.Keys
                .SelectMany(
                    t => typeof(DateTime).GetTypeInfo()
                        .GetDeclaredMethods(t)
                        .Where(
                            m => m.GetParameters()
                                     .Length == 1
                                 && _supportedTypes.Contains(
                                     m.GetParameters()
                                         .First()
                                         .ParameterType)))
                .ToList();

        public JetConvertTranslator(ISqlExpressionFactory sqlExpressionFactory)
            => _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;


        public SqlExpression Translate(SqlExpression instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (_supportedMethods.Contains(method) ||
                _supportedBoolParseMethods.Contains(method) ||
                _supportedDateTimeParseMethods.Contains(method) || 
                _supportedByteParseMethods.Contains(method) ||
                _supportedDecimalParseMethods.Contains(method) ||
                _supportedDoubleParseMethods.Contains(method) ||
                _supportedInt16ParseMethods.Contains(method) ||
                _supportedInt32ParseMethods.Contains(method) ||
                _supportedSingleParseMethods.Contains(method)
                )
            {
                return _sqlExpressionFactory.Convert(arguments[0], method.ReturnType);
            }
            return null;
        }
    }
}