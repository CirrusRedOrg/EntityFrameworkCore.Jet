using LibRed.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>LibRed-specific extension methods for <see cref="DbContextOptionsBuilder"/>.</summary>
public static class LibRedDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Configures the context to use the native LibRed engine against a Jet/ACE file. The
    /// connection string takes the form <c>Data Source=path\to\file.accdb</c> (a bare file path
    /// is also accepted).
    /// </summary>
    public static DbContextOptionsBuilder UseLibRed(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        var extension = (LibRedOptionsExtension)GetOrCreateExtension(optionsBuilder)
            .WithConnectionString(connectionString);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        return optionsBuilder;
    }

    public static DbContextOptionsBuilder<TContext> UseLibRed<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string connectionString)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseLibRed((DbContextOptionsBuilder)optionsBuilder, connectionString);

    private static LibRedOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.Options.FindExtension<LibRedOptionsExtension>()
           ?? new LibRedOptionsExtension();
}
