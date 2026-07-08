using EntityFrameworkCore.Jet.Infrastructure.Internal;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.LibRed.Storage.Internal;

/// <summary>
/// LibRed's relational type-mapping source. It reuses every EFCore.Jet mapping unchanged, but substitutes a
/// driver-free <see cref="long"/> mapping (<see cref="LibRedLongTypeMapping"/>) whose DbType reflects the type
/// the value is actually stored as (<c>decimal(20,0)</c>) rather than <c>Int64</c> — EFCore.Jet only reports
/// Decimal via an OLE DB / ODBC reflection poke that a native engine has no reason to reproduce.
/// </summary>
public class LibRedTypeMappingSource : JetTypeMappingSource
{
    public LibRedTypeMappingSource(
        TypeMappingSourceDependencies dependencies,
        RelationalTypeMappingSourceDependencies relationalDependencies,
        IJetOptions options)
        : base(dependencies, relationalDependencies, options)
    {
    }

    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        RelationalTypeMapping? mapping = base.FindMapping(mappingInfo);
        return mapping is JetLongTypeMapping ? LibRedLongTypeMapping.Default : mapping;
    }
}
