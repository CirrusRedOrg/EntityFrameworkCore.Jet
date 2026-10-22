// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;

namespace EntityFrameworkCore.LibRed.Storage.Internal
{
    public class LibRedDateTimeOffsetTypeMapping : DateTimeOffsetTypeMapping
    {
        private const string DateTimeOffsetFormatConst = @"'{0:yyyy-MM-ddTHH:mm:ss.fffffffzzz}'";
        private const string DateTimeFormatConst = @"'{0:yyyy-MM-dd HH:mm:ss}'";
        private const string DateTimeMillisecondsFormatConst = @"'{0:yyyy-MM-dd HH:mm:ss.fff}'";

        private static readonly MethodInfo FromStoredMethod
            = typeof(LibRedDateTimeOffsetTypeMapping).GetMethod(nameof(FromStored), [typeof(DateTimeOffset)])!;

        public static new LibRedDateTimeOffsetTypeMapping Default { get; } = new LibRedDateTimeOffsetTypeMapping("datetime");
        public LibRedDateTimeOffsetTypeMapping(
                string storeType)
            : base(
                storeType, System.Data.DbType.DateTime)
        {
        }

        protected LibRedDateTimeOffsetTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new LibRedDateTimeOffsetTypeMapping(parameters);

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

        // The read half of the substitution above: the OLE epoch, where default(DateTimeOffset) is stored, reads back
        // as default. It applies to EF's reads only (query shaping and SaveChanges read-back); LibRedDataReader keeps
        // returning the stored date, as ACE does, because other consumers (linq2db) rely on that.
        public override Expression CustomizeDataReaderExpression(Expression expression)
            => Expression.Call(FromStoredMethod, base.CustomizeDataReaderExpression(expression));

        public static DateTimeOffset FromStored(DateTimeOffset value)
            => value.UtcDateTime == DateTime.FromOADate(0) ? default : value;

        protected override string SqlLiteralFormatString
            => DateTimeOffsetFormatConst;

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            if (value is not DateTimeOffset offset) return base.GenerateNonNullSqlLiteral(value);
            var dateTime = offset.Ticks == 0 ? DateTime.FromOADate(0) : offset.UtcDateTime;
            var format = dateTime.Millisecond != 0 ? DateTimeMillisecondsFormatConst : DateTimeFormatConst;
            return $"CDATE({string.Format(CultureInfo.InvariantCulture, format, dateTime)})";
        }
    }
}