// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using System.Data.Jet;
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
        #region Connection String
        
        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="connectionString"> The connection string of the database to connect to. The underlying data
        /// access provider (ODBC or OLE DB) will be inferred from the style of this connection string. </param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder<TContext> UseJet<TContext>(
            [NotNull] this DbContextOptionsBuilder<TContext> optionsBuilder,
            [NotNull] string connectionString,
            [CanBeNull] Action<JetDbContextOptionsBuilder> jetOptionsAction = null)
            where TContext : DbContext
            => (DbContextOptionsBuilder<TContext>) UseJet((DbContextOptionsBuilder) optionsBuilder, connectionString, jetOptionsAction);

        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="connectionString"> The connection string of the database to connect to. The underlying data
        /// access provider (ODBC or OLE DB) will be inferred from the style of this connection string. </param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder UseJet(
            [NotNull] this DbContextOptionsBuilder optionsBuilder,
            [NotNull] string connectionString,
            [CanBeNull] Action<JetDbContextOptionsBuilder> jetOptionsAction = null)
        {
            Check.NotNull(optionsBuilder, nameof(optionsBuilder));
            Check.NotEmpty(connectionString, nameof(connectionString));

            return UseJetCore(optionsBuilder, connectionString, null, JetConnection.GetDataAccessProviderType(connectionString), jetOptionsAction);
        }

        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="connectionString"> The connection string of the database to connect to. </param>
        /// <param name="dataAccessProviderFactory">An `OdbcFactory` or `OleDbFactory` object to be used for all
        /// data access operations by the Jet connection.</param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder<TContext> UseJet<TContext>(
            [NotNull] this DbContextOptionsBuilder<TContext> optionsBuilder,
            [NotNull] string connectionString,
            [NotNull] DbProviderFactory dataAccessProviderFactory,
            [CanBeNull] Action<JetDbContextOptionsBuilder> jetOptionsAction = null)
            where TContext : DbContext
        {
            Check.NotNull(optionsBuilder, nameof(optionsBuilder));
            Check.NotEmpty(connectionString, nameof(connectionString));
            Check.NotNull(dataAccessProviderFactory, nameof(dataAccessProviderFactory));

            return (DbContextOptionsBuilder<TContext>) UseJet((DbContextOptionsBuilder) optionsBuilder, connectionString, dataAccessProviderFactory, jetOptionsAction);
        }

        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="connectionString"> The connection string of the database to connect to. </param>
        /// <param name="dataAccessProviderFactory">An `OdbcFactory` or `OleDbFactory` object to be used for all
        /// data access operations by the Jet connection.</param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder UseJet(
            [NotNull] this DbContextOptionsBuilder optionsBuilder,
            [NotNull] string connectionString,
            [NotNull] DbProviderFactory dataAccessProviderFactory,
            [CanBeNull] Action<JetDbContextOptionsBuilder> jetOptionsAction = null)
        {
            Check.NotNull(optionsBuilder, nameof(optionsBuilder));
            Check.NotEmpty(connectionString, nameof(connectionString));
            Check.NotNull(dataAccessProviderFactory, nameof(dataAccessProviderFactory));

            return UseJetCore(optionsBuilder, connectionString, dataAccessProviderFactory, null, jetOptionsAction);
        }

        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="connectionString"> The connection string of the database to connect to. </param>
        /// <param name="dataAccessProviderType">The type of the data access provider (`Odbc` or `OleDb`) to be used for all
        /// data access operations by the Jet connection.</param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder<TContext> UseJet<TContext>(
            [NotNull] this DbContextOptionsBuilder<TContext> optionsBuilder,
            [NotNull] string connectionString,
            DataAccessProviderType dataAccessProviderType,
            [CanBeNull] Action<JetDbContextOptionsBuilder> jetOptionsAction = null)
            where TContext : DbContext
        {
            Check.NotNull(optionsBuilder, nameof(optionsBuilder));
            Check.NotEmpty(connectionString, nameof(connectionString));

            return (DbContextOptionsBuilder<TContext>) UseJet((DbContextOptionsBuilder)optionsBuilder, connectionString, dataAccessProviderType, jetOptionsAction);
        }

        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="connectionString"> The connection string of the database to connect to. </param>
        /// <param name="dataAccessProviderType">The type of the data access provider (`Odbc` or `OleDb`) to be used for all
        /// data access operations by the Jet connection.</param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder UseJet(
            [NotNull] this DbContextOptionsBuilder optionsBuilder,
            [NotNull] string connectionString,
            DataAccessProviderType dataAccessProviderType,
            [CanBeNull] Action<JetDbContextOptionsBuilder> jetOptionsAction = null)
        {
            Check.NotNull(optionsBuilder, nameof(optionsBuilder));
            Check.NotEmpty(connectionString, nameof(connectionString));

            return UseJetCore(optionsBuilder, connectionString, null, dataAccessProviderType, jetOptionsAction);
        }

        internal static DbContextOptionsBuilder UseJetWithoutPredefinedDataAccessProvider(
            [NotNull] this DbContextOptionsBuilder optionsBuilder,
            [NotNull] string connectionString,
            [CanBeNull] Action<JetDbContextOptionsBuilder> jetOptionsAction = null)
            => UseJetCore(optionsBuilder, connectionString, null, null, jetOptionsAction);

        private static DbContextOptionsBuilder UseJetCore(
            [NotNull] DbContextOptionsBuilder optionsBuilder,
            [NotNull] string connectionString,
            [CanBeNull] DbProviderFactory dataAccessProviderFactory,
            [CanBeNull] DataAccessProviderType? dataAccessProviderType,
            Action<JetDbContextOptionsBuilder> jetOptionsAction)
        {
            Check.NotNull(optionsBuilder, nameof(optionsBuilder));
            Check.NotEmpty(connectionString, nameof(connectionString));

            if (dataAccessProviderFactory == null && dataAccessProviderType == null)
            {
                throw new ArgumentException($"One of the parameters {nameof(dataAccessProviderFactory)} and {nameof(dataAccessProviderType)} must not be null.");
            }

            var extension = (JetOptionsExtension) GetOrCreateExtension(optionsBuilder)
                .WithConnectionString(connectionString);

            extension = extension.WithDataAccessProviderFactory(
                dataAccessProviderFactory ?? JetFactory.Instance.GetDataAccessProviderFactory(dataAccessProviderType.Value));

            ((IDbContextOptionsBuilderInfrastructure) optionsBuilder).AddOrUpdateExtension(extension);

            ConfigureWarnings(optionsBuilder);

            jetOptionsAction?.Invoke(new JetDbContextOptionsBuilder(optionsBuilder));

            return optionsBuilder;
        }

        #endregion
        
        #region Connection
        
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
            => (DbContextOptionsBuilder<TContext>) UseJet(
                (DbContextOptionsBuilder) optionsBuilder, connection, jetOptionsAction);

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

            var jetConnection = connection as JetConnection;
            if (jetConnection == null)
            {
                throw new ArgumentException($"The {nameof(connection)} parameter must be of type {nameof(JetConnection)}.");
            }

            if (jetConnection.DataAccessProviderFactory == null)
            {
                var dataAccessProviderType = JetConnection.GetDataAccessProviderType(jetConnection.ConnectionString);
                jetConnection.DataAccessProviderFactory = JetFactory.Instance.GetDataAccessProviderFactory(dataAccessProviderType);
                jetConnection.Freeze();
            }

            var extension = (JetOptionsExtension) GetOrCreateExtension(optionsBuilder)
                .WithConnection(connection);

            extension = extension.WithDataAccessProviderFactory(jetConnection.DataAccessProviderFactory);

            ((IDbContextOptionsBuilderInfrastructure) optionsBuilder).AddOrUpdateExtension(extension);

            ConfigureWarnings(optionsBuilder);

            jetOptionsAction?.Invoke(new JetDbContextOptionsBuilder(optionsBuilder));

            return optionsBuilder;
        }

        #endregion
        
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

            ((IDbContextOptionsBuilderInfrastructure) optionsBuilder).AddOrUpdateExtension(coreOptionsExtension);
        }
    }
}