using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.LibRed.Storage.Internal;

public class LibRedTimeOnlyTypeMapping : TimeOnlyTypeMapping
{
    public static new LibRedTimeOnlyTypeMapping Default { get; } = new LibRedTimeOnlyTypeMapping("time");

    public LibRedTimeOnlyTypeMapping(
            string storeType)
        : base(storeType)
    {
    }

    protected LibRedTimeOnlyTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);
        if (parameter.Value is TimeOnly timeOnly)
        {
            timeOnly.Deconstruct(out int hour, out int min, out int sec, out int msec);
            parameter.Value = new TimeSpan(0, hour, min, sec, msec);
        }
    }

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new LibRedTimeOnlyTypeMapping(parameters);

    protected override string GenerateNonNullSqlLiteral(object value)
    {
        return ((TimeOnly)value).Millisecond != 0
            ? FormattableString.Invariant($@"TIMEVALUE('{value:HH\:mm\:ss\.fff}')")
            : FormattableString.Invariant($@"TIMEVALUE('{value:HH\:mm\:ss}')");
    }

    protected override string ProcessStoreType(RelationalTypeMappingParameters parameters, string storeType, string storeTypeNameBase)
    {
        return base.ProcessStoreType(parameters, storeTypeNameBase, storeTypeNameBase);
    }
}
