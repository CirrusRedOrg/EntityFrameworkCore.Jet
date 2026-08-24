using EntityFrameworkCore.Jet.Infrastructure.Internal;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.LibRed.Storage.Internal;

/// <summary>
/// LibRed's relational type-mapping source. It reuses every EFCore.Jet mapping unchanged except where LibRed's
/// engine can do something ACE cannot:
/// <list type="bullet">
///   <item><description>
///     <see cref="LibRedLongTypeMapping"/> — a driver-free <see cref="long"/> whose DbType reflects the type the
///     value is actually stored as (<c>decimal(20,0)</c>) rather than <c>Int64</c>; EFCore.Jet only reports
///     Decimal via an OLE DB / ODBC reflection poke that a native engine has no reason to reproduce.
///   </description></item>
///   <item><description>
///     The four temporal mappings — <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="TimeOnly"/>
///     and <see cref="TimeSpan"/> — whose literals carry MILLISECONDS. ACE truncates to whole seconds on write,
///     so EFCore.Jet has no reason to emit a fraction; LibRed stores the full OA double and a millisecond
///     survives it exactly. <c>LibRedCommand.Normalize</c> truncates parameters to the same boundary so the two
///     paths agree.
///   </description></item>
/// </list>
/// <see cref="DateOnly"/> deliberately has no LibRed variant: it carries no time component, so there is nothing
/// below a second for it to lose.
/// </summary>
public class LibRedTypeMappingSource(
    TypeMappingSourceDependencies dependencies,
    RelationalTypeMappingSourceDependencies relationalDependencies,
    IJetOptions options)
    : JetTypeMappingSource(dependencies, relationalDependencies, options)
{
    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        RelationalTypeMapping? mapping = base.FindMapping(mappingInfo);

        // Substituted by the mapping Jet resolved rather than by CLR type, so anything Jet decides about store
        // type, size or nullability is preserved — only the literal/parameter behaviour is replaced. Clone
        // carries the resolved parameters across.
        return mapping switch
        {
            JetLongTypeMapping => LibRedLongTypeMapping.Default,
            JetDateTimeTypeMapping => Retarget(mapping, LibRedDateTimeTypeMapping.Default),
            JetDateTimeOffsetTypeMapping => Retarget(mapping, LibRedDateTimeOffsetTypeMapping.Default),
            JetTimeOnlyTypeMapping => Retarget(mapping, LibRedTimeOnlyTypeMapping.Default),
            JetTimeSpanTypeMapping => Retarget(mapping, LibRedTimeSpanTypeMapping.Default),
            _ => mapping,
        };
    }

    /// <summary>Rebuilds <paramref name="replacement"/> with the store type, size and facets Jet resolved.</summary>
    private static RelationalTypeMapping Retarget(RelationalTypeMapping resolved, RelationalTypeMapping replacement)
        => replacement.WithStoreTypeAndSize(resolved.StoreType, resolved.Size);
}
