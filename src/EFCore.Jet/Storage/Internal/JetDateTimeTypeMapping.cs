// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using EntityFrameworkCore.Jet.Data;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDateTimeTypeMapping : RelationalTypeMapping
    {
        private const int MaxDateTimeDoublePrecision = 10;
        private static readonly JetDecimalTypeMapping _decimalTypeMapping = new JetDecimalTypeMapping("decimal", System.Data.DbType.Decimal, 18, 10);
        
        public JetDateTimeTypeMapping(
            [NotNull] string storeType,
            DbType? dbType = null,
            [CanBeNull] Type clrType = null)
            : base(storeType, clrType ?? typeof(DateTime), dbType ?? System.Data.DbType.DateTime)
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
            base.ConfigureParameter(parameter);
            
            if (parameter.Value is DateTime dateTime)
            {
                parameter.Value = GetDateTimeDoubleValueAsDecimal(dateTime);
                parameter.ResetDbType();
            }
        }

        protected override string GenerateNonNullSqlLiteral(object value)
            => GenerateNonNullSqlLiteral(value, false);

        public static string GenerateNonNullSqlLiteral(object value, bool defaultClauseCompatible)
        {
            var dateTime = (DateTime) value;

            dateTime = CheckDateTimeValue(dateTime);
            
            if (!defaultClauseCompatible /*&&
                dateTime.Ticks % TimeSpan.TicksPerSecond == 0*/)
            {
                var literal = new StringBuilder()
                    .AppendFormat(CultureInfo.InvariantCulture, "#{0:yyyy-MM-dd}", dateTime);
                
                var time = dateTime.TimeOfDay;
                if (time != TimeSpan.Zero)
                {
                    literal.AppendFormat(CultureInfo.InvariantCulture, @" {0:hh\:mm\:ss}", time);
                }

                literal.Append("#");
                
                if (time != TimeSpan.Zero)
                {
                    // Round to milliseconds.
                    var millisecondsTicks = time.Ticks % TimeSpan.TicksPerSecond / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond;
                    if (millisecondsTicks > 0)
                    {
                        var jetTimeDoubleFractions = Math.Round((decimal) millisecondsTicks / TimeSpan.TicksPerDay, MaxDateTimeDoublePrecision);

                        literal
                            .Insert(0, "(")
                            .Append(" + ")
                            .Append(_decimalTypeMapping.GenerateSqlLiteral(jetTimeDoubleFractions))
                            .Append(")");
                    }
                }

                return literal.ToString();
            }

            return _decimalTypeMapping.GenerateSqlLiteral(GetDateTimeDoubleValueAsDecimal(dateTime));
        }

        private static decimal GetDateTimeDoubleValueAsDecimal(DateTime dateTime)
        {
            var checkDateTimeValue = CheckDateTimeValue(dateTime) - JetConfiguration.TimeSpanOffset;
            
            // Round to milliseconds.
            var millisecondsTicks = checkDateTimeValue.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond;
            var result = Math.Round((decimal) millisecondsTicks / TimeSpan.TicksPerDay, MaxDateTimeDoublePrecision);

            return result;
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