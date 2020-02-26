// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using EntityFrameworkCore.Jet.Infrastructure;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using EntityFrameworkCore.Jet.Utilities;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    ///     Jet specific extension methods for <see cref="DbContextOptionsBuilder" />.
    /// </summary>
    public static class JetDbContextOptionsBuilderExtensions
    {
        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="connectionString"> The connection string of the database to connect to. </param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder UseJet(
            [NotNull] this DbContextOptionsBuilder optionsBuilder,
            [NotNull] string connectionString,
            [CanBeNull] Action<JetDbContextOptionsBuilder> jetOptionsAction = null)
        {
            Check.NotNull(optionsBuilder, nameof(optionsBuilder));
            Check.NotEmpty(connectionString, nameof(connectionString));

            var extension = (JetOptionsExtension)GetOrCreateExtension(optionsBuilder).WithConnectionString(connectionString);
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

            ConfigureWarnings(optionsBuilder);

            jetOptionsAction?.Invoke(new JetDbContextOptionsBuilder(optionsBuilder));

            return optionsBuilder;
        }

        // Note: Decision made to use DbConnection not SqlConnection: Issue #772
        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="connection">
        ///     An existing <see cref="DbConnection" /> to be used to connect to the database. If the connection is
        ///     in the open state then EF will not open or close the connection. If the connection is in the closed
        ///     state then EF will open and close the connection as needed.
        /// </param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder UseJet(
            [NotNull] this DbContextOptionsBuilder optionsBuilder,
            [NotNull] DbConnection connection,
            [CanBeNull] Action<JetDbContextOptionsBuilder> jetOptionsAction = null)
        {
            Check.NotNull(optionsBuilder, nameof(optionsBuilder));
            Check.NotNull(connection, nameof(connection));

            var extension = (JetOptionsExtension)GetOrCreateExtension(optionsBuilder).WithConnection(connection);
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

            ConfigureWarnings(optionsBuilder);

            jetOptionsAction?.Invoke(new JetDbContextOptionsBuilder(optionsBuilder));

            return optionsBuilder;
        }

        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <typeparam name="TContext"> The type of context to be configured. </typeparam>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="connectionString"> The connection string of the database to connect to. </param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder<TContext> UseJet<TContext>(
            [NotNull] this DbContextOptionsBuilder<TContext> optionsBuilder,
            [NotNull] string connectionString,
            [CanBeNull] Action<JetDbContextOptionsBuilder> jetOptionsAction = null)
            where TContext : DbContext
            => (DbContextOptionsBuilder<TContext>)UseJet(
                (DbContextOptionsBuilder)optionsBuilder, connectionString, jetOptionsAction);

        // Note: Decision made to use DbConnection not SqlConnection: Issue #772
        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <typeparam name="TContext"> The type of context to be configured. </typeparam>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="connection">
        ///     An existing <see cref="DbConnection" /> to be used to connect to the database. If the connection is
        ///     in the open state then EF will not open or close the connection. If the connection is in the closed
        ///     state then EF will open and close the connection as needed.
        /// </param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder<TContext> UseJet<TContext>(
            [NotNull] this DbContextOptionsBuilder<TContext> optionsBuilder,
            [NotNull] DbConnection connection,
            [CanBeNull] Action<JetDbContextOptionsBuilder> jetOptionsAction = null)
            where TContext : DbContext
            => (DbContextOptionsBuilder<TContext>)UseJet(
                (DbContextOptionsBuilder)optionsBuilder, connection, jetOptionsAction);

        private static JetOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.Options.FindExtension<JetOptionsExtension>()
               ?? new JetOptionsExtension();

        private static void ConfigureWarnings(DbContextOptionsBuilder optionsBuilder)
        {
            var coreOptionsExtension
                = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()
                  ?? new CoreOptionsExtension();

            coreOptionsExtension = coreOptionsExtension.WithWarningsConfiguration(
                coreOptionsExtension.WarningsConfiguration.TryWithExplicit(
                    RelationalEventId.AmbientTransactionWarning, WarningBehavior.Throw));

            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(coreOptionsExtension);
        }
    }
}
