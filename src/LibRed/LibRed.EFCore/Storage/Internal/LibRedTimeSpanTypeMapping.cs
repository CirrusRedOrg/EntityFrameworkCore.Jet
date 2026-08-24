using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.LibRed.Storage.Internal;

public class LibRedTimeSpanTypeMapping : TimeSpanTypeMapping
{
    public static new LibRedTimeSpanTypeMapping Default { get; } = new LibRedTimeSpanTypeMapping("datetime");

    public LibRedTimeSpanTypeMapping(
            string storeType)
        : base(storeType)
    {
    }

    protected LibRedTimeSpanTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new LibRedTimeSpanTypeMapping(parameters);

    /*protected override DateTime ConvertToDateTimeCompatibleValue(object value)
            => JetConfiguration.TimeSpanOffset + (TimeSpan)value;*/

    protected override string ProcessStoreType(RelationalTypeMappingParameters parameters, string storeType, string storeTypeNameBase)
    {
        return base.ProcessStoreType(parameters, storeTypeNameBase, storeTypeNameBase);
    }

    protected override string GenerateNonNullSqlLiteral(object value)
    {
        return ((TimeSpan)value).Milliseconds != 0
            ? FormattableString.Invariant($@"TIMEVALUE('{value:hh\:mm\:ss\.fff}')")
            : FormattableString.Invariant($@"TIMEVALUE('{value:hh\:mm\:ss}')");
    }
}
