using EntityFrameworkCore.Jet.Storage.Internal;

namespace EntityFrameworkCore.LibRed.Storage.Internal;

/// <summary>
///     This API supports the Entity Framework Core infrastructure and is not intended to be used
///     directly from your code. This API may change or be removed in future releases.
/// </summary>
/// <remarks>
///     No members of its own today — it exists so LibRed-specific services can depend on "the
///     LibRed connection" instead of the more general <see cref="IJetRelationalConnection" />.
/// </remarks>
public interface ILibRedRelationalConnection : IRelationalConnection
{
}
