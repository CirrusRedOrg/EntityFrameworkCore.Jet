// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data;
using System.Globalization;
using System.Text;

namespace EntityFrameworkCore.LibRed.Storage.Internal
{
    public class LibRedDateTimeTypeMapping : DateTimeTypeMapping
    {
        /// <summary>The lowest date Jet/ACE can represent; dates run 0100-01-01 to 9999-12-31.</summary>
        private static readonly DateTime LibRedMinDate = new(100, 1, 1);

        public static new LibRedDateTimeTypeMapping Default { get; } = new LibRedDateTimeTypeMapping("datetime", dbType: System.Data.DbType.DateTime);

        public LibRedDateTimeTypeMapping(
            string storeType,
            DbType? dbType = null,
            Type? clrType = null)
            : base(storeType)
        {
        }

        protected LibRedDateTimeTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new LibRedDateTimeTypeMapping(parameters);

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

                if (time.Milliseconds != 0)
                {
                    literal.AppendFormat(CultureInfo.InvariantCulture, @"{0:\.fff}", time);
                }
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
            // default(DateTime) is below Jet's floor, but every caller has already substituted the OLE epoch for
            // it, so anything still under the floor here is a real value the store cannot represent. Ordering
            // comparisons against default are corrected separately, in JetDateTimeRangeConverter.
            if (dateTime < LibRedMinDate)
            {
                throw new InvalidOperationException(
                    $"The {nameof(DateTime)} value '{dateTime}' is smaller than the minimum supported value of '{LibRedMinDate}'.");
            }

            return dateTime;
        }

        // Deliberately passes storeTypeNameBase in place of storeType: Jet/ACE has no scaled datetime, so a
        // precision-carrying store type such as "datetime(3)" must collapse to the bare "datetime" it understands.
        protected override string ProcessStoreType(RelationalTypeMappingParameters parameters, string storeType, string storeTypeNameBase)
        {
            return base.ProcessStoreType(parameters, storeTypeNameBase, storeTypeNameBase);
        }
    }
}
