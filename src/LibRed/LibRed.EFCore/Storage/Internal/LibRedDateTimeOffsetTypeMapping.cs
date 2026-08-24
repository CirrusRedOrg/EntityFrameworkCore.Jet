using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.LibRed.Storage.Internal;

public class LibRedDateTimeOffsetTypeMapping : DateTimeOffsetTypeMapping
{
    private const string DateTimeOffsetFormatConst = @"'{0:yyyy-MM-ddTHH:mm:ss.fffffffzzz}'";
    private const string DateTimeFormatConst = @"'{0:yyyy-MM-dd HH:mm:ss}'";
    private const string DateTimeMillisecondsFormatConst = @"'{0:yyyy-MM-dd HH:mm:ss.fff}'";

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
        var format = dateTime.Millisecond != 0 ? DateTimeMillisecondsFormatConst : DateTimeFormatConst;
        return $"CDATE({string.Format(CultureInfo.InvariantCulture, format, dateTime)})";
    }
}
