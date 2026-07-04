// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data;
using System.Globalization;
using System.Text;
using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.Infrastructure.Internal;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDateTimeTypeMapping : DateTimeTypeMapping
    {
        private const int MaxDateTimeDoublePrecision = 10;

        public static new JetDateTimeTypeMapping Default { get; } = new JetDateTimeTypeMapping("datetime", dbType: System.Data.DbType.DateTime);

        public JetDateTimeTypeMapping(
            string storeType,
            DbType? dbType = null,
            Type? clrType = null)
            : base(storeType)
        {
        }

        protected JetDateTimeTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetDateTimeTypeMapping(parameters);

        protected override void ConfigureParameter(DbParameter parameter)
        {
            if (parameter.Value is DateTime { Ticks: 0 })
            {
                parameter.Value = DateTime.FromOADate(0);
            }
            base.ConfigureParameter(parameter);

            if ((parameter.DbType == System.Data.DbType.Date || StoreTypeNameBase == "date") && parameter.Value is DateTime date)
            {
                parameter.Value = date.Date;
            }
        }

        protected override string GenerateNonNullSqlLiteral(object value)
            => GenerateNonNullSqlLiteral(value, false);

        public virtual string GenerateNonNullSqlLiteral(object value, bool defaultClauseCompatible)
        {
            var dateTime = ConvertToDateTimeCompatibleValue(value);
            if (dateTime is DateTime { Ticks: 0 })
            {
                dateTime = DateTime.FromOADate(0);
            }
            dateTime = CheckDateTimeValue(dateTime);

            var literal = new StringBuilder();

            literal.Append(
                defaultClauseCompatible
                    ? "'"
                    : "#");

            literal.AppendFormat(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd}", dateTime);

            var time = dateTime.TimeOfDay;
            if (time != TimeSpan.Zero && StoreTypeNameBase != "date")
            {
                literal.AppendFormat(CultureInfo.InvariantCulture, @" {0:hh\:mm\:ss}", time);
            }

            literal.Append(
                defaultClauseCompatible
                    ? "'"
                    : "#");

            return literal.ToString();
        }

        protected virtual DateTime ConvertToDateTimeCompatibleValue(object value)
            => (DateTime)value;

        private static DateTime CheckDateTimeValue(DateTime dateTime)
        {
            if (dateTime < new DateTime(100,1,1))
            {
                if (dateTime != default)
                {
                    throw new InvalidOperationException($"The {nameof(DateTime)} value '{dateTime}' is smaller than the minimum supported value of '{JetConfiguration.TimeSpanOffset}'.");
                }

                //dateTime = JetConfiguration.TimeSpanOffset;
            }

            return dateTime;
        }

        protected override string ProcessStoreType(RelationalTypeMappingParameters parameters, string storeType, string storeTypeNameBase)
        {
            return base.ProcessStoreType(parameters, storeTypeNameBase, storeTypeNameBase);
        }
    }
}