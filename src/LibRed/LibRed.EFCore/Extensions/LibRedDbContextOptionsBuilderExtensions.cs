// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data.Common;
using EntityFrameworkCore.LibRed.Infrastructure;
using EntityFrameworkCore.LibRed.Infrastructure.Internal;
using LibRed.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>LibRed-specific extension methods for <see cref="DbContextOptionsBuilder"/>.</summary>
public static class LibRedDbContextOptionsBuilderExtensions
{
    /// <summary>
    ///     Configures the context to connect to a Jet/ACE database via the native LibRed engine, but without
    ///     initially setting any <see cref="DbConnection" /> or connection string.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="libRedOptionsAction">An optional action to allow additional LibRed specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseLibRed(
        this DbContextOptionsBuilder optionsBuilder,
        Action<LibRedDbContextOptionsBuilder>? libRedOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(GetOrCreateExtension(optionsBuilder));

        return ApplyConfiguration(optionsBuilder, libRedOptionsAction);
    }

    /// <summary>
    ///     Configures the context to use the native LibRed engine against a Jet/ACE file. The
    ///     connection string takes the form <c>Data Source=path\to\file.accdb</c> (a bare file path
    ///     is also accepted).
    /// </summary>
    /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
    /// <param name="connectionString"> The file name or connection string of the database to connect to. </param>
    /// <param name="libRedOptionsAction">An optional action to allow additional LibRed specific configuration.</param>
    /// <returns> The options builder so that further configuration can be chained. </returns>
    public static DbContextOptionsBuilder UseLibRed(
        this DbContextOptionsBuilder optionsBuilder,
        string? connectionString,
        Action<LibRedDbContextOptionsBuilder>? libRedOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        var extension = (LibRedOptionsExtension)GetOrCreateExtension(optionsBuilder)
            .WithConnectionString(connectionString);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        return ApplyConfiguration(optionsBuilder, libRedOptionsAction);
    }

    /// <summary>
    ///     Configures the context to use the native LibRed engine against a Jet/ACE file. The
    ///     connection string takes the form <c>Data Source=path\to\file.accdb</c> (a bare file path
    ///     is also accepted).
    /// </summary>
    /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
    /// <param name="connectionString"> The file name or connection string of the database to connect to. </param>
    /// <param name="dataAccessProviderFactory">The <see cref="DbProviderFactory" /> to be used for all
    /// data access operations by the LibRed connection.</param>
    /// <param name="libRedOptionsAction">An optional action to allow additional LibRed specific configuration.</param>
    /// <returns> The options builder so that further configuration can be chained. </returns>
    public static DbContextOptionsBuilder UseLibRed(
        this DbContextOptionsBuilder optionsBuilder,
        string? connectionString,
        DbProviderFactory dataAccessProviderFactory,
        Action<LibRedDbContextOptionsBuilder>? libRedOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(dataAccessProviderFactory);

        var extension = (LibRedOptionsExtension)GetOrCreateExtension(optionsBuilder)
            .WithConnectionString(connectionString);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        return ApplyConfiguration(optionsBuilder, libRedOptionsAction);
    }

    // Note: Decision made to use DbConnection not SqlConnection: Issue #772
    /// <summary>
    ///     Configures the context to use the native LibRed engine.
    /// </summary>
    /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
    /// <param name="connection">
    ///     An existing <see cref="LibRedConnection" /> to be used to connect to the database. If the connection is
    ///     in the open state then EF will not open or close the connection. If the connection is in the closed
    ///     state then EF will open and close the connection as needed.
    /// </param>
    /// <param name="libRedOptionsAction">An optional action to allow additional LibRed specific configuration.</param>
    /// <returns> The options builder so that further configuration can be chained. </returns>
    public static DbContextOptionsBuilder UseLibRed(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        Action<LibRedDbContextOptionsBuilder>? libRedOptionsAction = null)
        => UseLibRed(optionsBuilder, connection, false, libRedOptionsAction);

    // Note: Decision made to use DbConnection not SqlConnection: Issue #772
    /// <summary>
    ///     Configures the context to use the native LibRed engine.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connection">
    ///     An existing <see cref="LibRedConnection" /> to be used to connect to the database. If the connection is
    ///     in the open state then EF will not open or close the connection. If the connection is in the closed
    ///     state then EF will open and close the connection as needed.
    /// </param>
    /// <param name="contextOwnsConnection">
    ///     If <see langword="true" />, then EF will take ownership of the connection and will
    ///     dispose it in the same way it would dispose a connection created by EF. If <see langword="false" />, then the caller still
    ///     owns the connection and is responsible for its disposal.
    /// </param>
    /// <param name="libRedOptionsAction">An optional action to allow additional LibRed specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseLibRed(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<LibRedDbContextOptionsBuilder>? libRedOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(connection);

        if (connection is not LibRedConnection)
        {
            throw new ArgumentException($"The {nameof(connection)} parameter must be of type {nameof(LibRedConnection)}.");
        }

        var extension = (LibRedOptionsExtension)GetOrCreateExtension(optionsBuilder)
            .WithConnection(connection, contextOwnsConnection);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        return ApplyConfiguration(optionsBuilder, libRedOptionsAction);
    }

    /// <summary>
    ///     Configures the context to connect to a Jet/ACE database via the native LibRed engine, but without
    ///     initially setting any <see cref="DbConnection" /> or connection string.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="libRedOptionsAction">An optional action to allow additional LibRed specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseLibRed<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        Action<LibRedDbContextOptionsBuilder>? libRedOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseLibRed(
            (DbContextOptionsBuilder)optionsBuilder, libRedOptionsAction);

    /// <summary>
    ///     Configures the context to use the native LibRed engine against a Jet/ACE file. The
    ///     connection string takes the form <c>Data Source=path\to\file.accdb</c> (a bare file path
    ///     is also accepted).
    /// </summary>
    /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
    /// <param name="connectionString"> The file name or connection string of the database to connect to. </param>
    /// <param name="libRedOptionsAction">An optional action to allow additional LibRed specific configuration.</param>
    /// <returns> The options builder so that further configuration can be chained. </returns>
    public static DbContextOptionsBuilder<TContext> UseLibRed<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string? connectionString,
        Action<LibRedDbContextOptionsBuilder>? libRedOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseLibRed(
            (DbContextOptionsBuilder)optionsBuilder, connectionString, libRedOptionsAction);

    /// <summary>
    ///     Configures the context to use the native LibRed engine against a Jet/ACE file. The
    ///     connection string takes the form <c>Data Source=path\to\file.accdb</c> (a bare file path
    ///     is also accepted).
    /// </summary>
    /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
    /// <param name="connectionString"> The file name or connection string of the database to connect to. </param>
    /// <param name="dataAccessProviderFactory">The <see cref="DbProviderFactory" /> to be used for all
    /// data access operations by the LibRed connection.</param>
    /// <param name="libRedOptionsAction">An optional action to allow additional LibRed specific configuration.</param>
    /// <returns> The options builder so that further configuration can be chained. </returns>
    public static DbContextOptionsBuilder<TContext> UseLibRed<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string? connectionString,
        DbProviderFactory dataAccessProviderFactory,
        Action<LibRedDbContextOptionsBuilder>? libRedOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseLibRed(
            (DbContextOptionsBuilder)optionsBuilder, connectionString, dataAccessProviderFactory, libRedOptionsAction);

    // Note: Decision made to use DbConnection not SqlConnection: Issue #772
    /// <summary>
    ///     Configures the context to use the native LibRed engine.
    /// </summary>
    /// <typeparam name="TContext"> The type of context to be configured. </typeparam>
    /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
    /// <param name="connection">
    ///     An existing <see cref="LibRedConnection" /> to be used to connect to the database. If the connection is
    ///     in the open state then EF will not open or close the connection. If the connection is in the closed
    ///     state then EF will open and close the connection as needed.
    /// </param>
    /// <param name="libRedOptionsAction">An optional action to allow additional LibRed specific configuration.</param>
    /// <returns> The options builder so that further configuration can be chained. </returns>
    public static DbContextOptionsBuilder<TContext> UseLibRed<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        Action<LibRedDbContextOptionsBuilder>? libRedOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseLibRed(
            (DbContextOptionsBuilder)optionsBuilder, connection, libRedOptionsAction);

    // Note: Decision made to use DbConnection not SqlConnection: Issue #772
    /// <summary>
    ///     Configures the context to use the native LibRed engine.
    /// </summary>
    /// <typeparam name="TContext">The type of context to be configured.</typeparam>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connection">
    ///     An existing <see cref="LibRedConnection" /> to be used to connect to the database. If the connection is
    ///     in the open state then EF will not open or close the connection. If the connection is in the closed
    ///     state then EF will open and close the connection as needed.
    /// </param>
    /// <param name="contextOwnsConnection">
    ///     If <see langword="true" />, then EF will take ownership of the connection and will
    ///     dispose it in the same way it would dispose a connection created by EF. If <see langword="false" />, then the caller still
    ///     owns the connection and is responsible for its disposal.
    /// </param>
    /// <param name="libRedOptionsAction">An optional action to allow additional LibRed specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseLibRed<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<LibRedDbContextOptionsBuilder>? libRedOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseLibRed(
            (DbContextOptionsBuilder)optionsBuilder, connection, contextOwnsConnection, libRedOptionsAction);

    private static LibRedOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.Options.FindExtension<LibRedOptionsExtension>()
           ?? new LibRedOptionsExtension();

    private static DbContextOptionsBuilder ApplyConfiguration(
        DbContextOptionsBuilder optionsBuilder,
        Action<LibRedDbContextOptionsBuilder>? libRedOptionsAction)
    {
        ConfigureWarnings(optionsBuilder);

        libRedOptionsAction?.Invoke(new LibRedDbContextOptionsBuilder(optionsBuilder));

        var extension = GetOrCreateExtension(optionsBuilder);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        return optionsBuilder;
    }

    private static void ConfigureWarnings(DbContextOptionsBuilder optionsBuilder)
    {
        var coreOptionsExtension
            = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()
              ?? new CoreOptionsExtension();

        coreOptionsExtension = RelationalOptionsExtension.WithDefaultWarningConfiguration(coreOptionsExtension);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(coreOptionsExtension);
    }
}
