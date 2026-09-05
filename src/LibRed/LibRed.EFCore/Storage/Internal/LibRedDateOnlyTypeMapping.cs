// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data;
using System.Globalization;
using System.Text;

namespace EntityFrameworkCore.LibRed.Storage.Internal
{
    public class LibRedDateOnlyTypeMapping : DateOnlyTypeMapping
    {

        public static new LibRedDateOnlyTypeMapping Default { get; } = new LibRedDateOnlyTypeMapping("date", dbType: System.Data.DbType.Date);
        public LibRedDateOnlyTypeMapping(
            string storeType,
            DbType? dbType = null)
            : base(storeType)
        {
        }

        protected LibRedDateOnlyTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new LibRedDateOnlyTypeMapping(parameters);

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
