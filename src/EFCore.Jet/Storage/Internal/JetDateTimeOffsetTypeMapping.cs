// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Infrastructure.Internal;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDateTimeOffsetTypeMapping : DateTimeOffsetTypeMapping
    {
        private const string DateTimeOffsetFormatConst = @"'{0:yyyy-MM-ddTHH:mm:ss.fffffffzzz}'";
        private const string DateTimeFormatConst = @"'{0:yyyy-MM-dd HH:mm:ss}'";

        public static new JetDateTimeOffsetTypeMapping Default { get; } = new JetDateTimeOffsetTypeMapping("datetime");
        public JetDateTimeOffsetTypeMapping(
                string storeType)
            : base(
                storeType, System.Data.DbType.DateTime)
        {
        }

        protected JetDateTimeOffsetTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetDateTimeOffsetTypeMapping(parameters);

        protected override void ConfigureParameter(DbParameter parameter)
        {
            // OLE DB can't handle the DateTimeOffset type.
            if (parameter.Value is DateTimeOffset dateTimeOffset)
            {
                parameter.Value = dateTimeOffset.Ticks == 0 ? DateTime.FromOADate(0) : dateTimeOffset.UtcDateTime;
                parameter.DbType = System.Data.DbType.DateTime;
            }

            base.ConfigureParameter(parameter);
        }

        protected override string SqlLiteralFormatString
            => DateTimeOffsetFormatConst;

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            if (value is not DateTimeOffset offset) return base.GenerateNonNullSqlLiteral(value);
            var dateTime = offset.Ticks == 0 ? DateTime.FromOADate(0) : offset.UtcDateTime;
            return $"CDATE({string.Format(CultureInfo.InvariantCulture, DateTimeFormatConst, dateTime)})";
        }
    }
}