// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data;
using System.Globalization;
using System.Text;
using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.Infrastructure.Internal;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDateOnlyTypeMapping : DateOnlyTypeMapping
    {
        private readonly IJetOptions _options;

        public JetDateOnlyTypeMapping(
            string storeType,
            IJetOptions options,
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
            if (parameter.Value is DateOnly dateOnly)
            {
                dateOnly.Deconstruct(out int year, out int month, out int day);
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
            if (dateTime != default && dateTime < new DateTime(100,1,1))
            {
                throw new InvalidOperationException($"The {nameof(DateTime)} value '{dateTime}' is smaller than the minimum supported value of '{new DateTime(100, 1, 1)}'.");
            }

            return dateTime;
        }

        protected override string ProcessStoreType(RelationalTypeMappingParameters parameters, string storeType, string storeTypeNameBase)
        {
            return base.ProcessStoreType(parameters, storeTypeNameBase, storeTypeNameBase);
        }
    }
}