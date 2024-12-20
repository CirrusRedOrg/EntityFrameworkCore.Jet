// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;
using EntityFrameworkCore.Jet.Infrastructure.Internal;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDateTimeOffsetTypeMapping : DateTimeOffsetTypeMapping
    {
        private readonly IJetOptions _options;
        private const string DateTimeOffsetFormatConst = @"'{0:yyyy-MM-ddTHH:mm:ss.fffffffzzz}'";
        private const string DateTimeFormatConst = @"'{0:yyyy-MM-dd HH:mm:ss}'";
        public JetDateTimeOffsetTypeMapping(
                string storeType,
                IJetOptions options)
            : base(
                storeType, System.Data.DbType.DateTime)
        {
            _options = options;
        }

        protected JetDateTimeOffsetTypeMapping(RelationalTypeMappingParameters parameters, IJetOptions options)
            : base(parameters)
        {
            _options = options;
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetDateTimeOffsetTypeMapping(parameters, _options);

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