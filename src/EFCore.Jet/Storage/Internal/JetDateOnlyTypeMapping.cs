// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection.Metadata;
using System.Text;
using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDateOnlyTypeMapping : DateOnlyTypeMapping
    {
        private readonly IJetOptions _options;

        public JetDateOnlyTypeMapping(
            [NotNull] string storeType,
            [NotNull] IJetOptions options,
            DbType? dbType = null)
            : base(storeType)
        {
            _options = options;
        }

        protected JetDateOnlyTypeMapping(RelationalTypeMappingParameters parameters, IJetOptions options)
            : base(parameters)
        {
            _options = options;
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetDateOnlyTypeMapping(parameters, _options);

        protected override void ConfigureParameter(DbParameter parameter)
        {
            base.ConfigureParameter(parameter);
            if (parameter.Value != null)
            {
                ((DateOnly)parameter.Value).Deconstruct(out int year, out int month, out int day);
                parameter.Value = new DateTime(year, month, day);
            }
        }

        protected override string GenerateNonNullSqlLiteral(object value)
            => GenerateNonNullSqlLiteral(value, false);

        public virtual string GenerateNonNullSqlLiteral(object value, bool defaultClauseCompatible)
        {
            var dateTime = ConvertToDateTimeCompatibleValue(value);

            dateTime = CheckDateTimeValue(dateTime);

            var literal = new StringBuilder();

            literal.Append(
                defaultClauseCompatible
                    ? "'"
                    : "#");

            literal.AppendFormat(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd}", dateTime);
            literal.Append(
                defaultClauseCompatible
                    ? "'"
                    : "#");

            return literal.ToString();
        }

        protected virtual DateTime ConvertToDateTimeCompatibleValue(object value)
        {
            ((DateOnly)value).Deconstruct(out int year, out int month, out int day);
            return new DateTime(year, month, day);
        }

        private static DateTime CheckDateTimeValue(DateTime dateTime)
        {
            if (dateTime < JetConfiguration.TimeSpanOffset)
            {
                if (dateTime != default)
                {
                    throw new InvalidOperationException($"The {nameof(DateTime)} value '{dateTime}' is smaller than the minimum supported value of '{JetConfiguration.TimeSpanOffset}'.");
                }

                dateTime = JetConfiguration.TimeSpanOffset;
            }

            return dateTime;
        }
    }
}