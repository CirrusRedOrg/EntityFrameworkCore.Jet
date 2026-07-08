using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace EntityFrameworkCore.LibRed.Storage.Internal;

/// <summary>
/// A <see cref="long"/> is emulated as <c>decimal(20,0)</c> on disk — Jet/ACE has no native 64-bit integer
/// before ACE 16, and LibRed targets Access 2007 (ACE 12) files. EFCore.Jet's <c>JetLongTypeMapping</c> keeps
/// its DbType as <see cref="DbType.Int64"/> and, at parameter-configuration time, pokes the underlying
/// OLE DB / ODBC parameter to the Numeric type via reflection to work around an x86/x64 conversion bug in those
/// drivers. LibRed has no such driver, so it simply maps the DbType to <see cref="DbType.Decimal"/> directly —
/// which is what the value is actually stored as — with no reflection and no driver dependency.
/// </summary>
public class LibRedLongTypeMapping : LongTypeMapping
{
    public static new LibRedLongTypeMapping Default { get; } = new();

    private LibRedLongTypeMapping()
        : base(new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(typeof(long), jsonValueReaderWriter: JsonInt64ReaderWriter.Instance),
                "decimal(20, 0)",
                StoreTypePostfix.PrecisionAndScale,
                System.Data.DbType.Decimal)
            .WithPrecisionAndScale(20, 0))
    {
    }

    protected LibRedLongTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new LibRedLongTypeMapping(parameters);
}
