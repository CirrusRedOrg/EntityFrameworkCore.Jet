// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using EntityFrameworkCore.Jet.Data;
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
        public static DbContextOptionsBuilder UseJet(
            this DbContextOptionsBuilder optionsBuilder,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
        {
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(GetOrCreateExtension(optionsBuilder));

            return ApplyConfiguration(optionsBuilder, jetOptionsAction);
        }

        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="fileNameOrConnectionString"> The file name or connection string of the database to connect to.
        /// If just a file name is supplied, the default data access provider type as defined by
        /// `JetConfiguration.DefaultDataAccessProviderType` is being used. If a connection string is supplied, the
        /// underlying data access provider (ODBC or OLE DB) will be inferred from the style of the connection string.
        /// In case the connection string does not specify an Access driver (ODBC) or ACE/Jet provider (OLE DB), the
        /// highest version of all compatible installed ones is being used. </param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder UseJet(
            this DbContextOptionsBuilder optionsBuilder,
            string? fileNameOrConnectionString,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
        {
            return UseJetCore(optionsBuilder, fileNameOrConnectionString, null, null, jetOptionsAction);
        }

        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="fileNameOrConnectionString"> The file name or connection string of the database to connect to.</param>
        /// <param name="dataAccessProviderFactory">An `OdbcFactory` or `OleDbFactory` object to be used for all
        /// data access operations by the Jet connection.</param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder<TContext> UseJet<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder,
            string? fileNameOrConnectionString,
            DbProviderFactory dataAccessProviderFactory,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
            where TContext : DbContext
        {
            return (DbContextOptionsBuilder<TContext>)UseJet((DbContextOptionsBuilder)optionsBuilder, fileNameOrConnectionString, dataAccessProviderFactory, jetOptionsAction);
        }

        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="fileNameOrConnectionString"> The file name or connection string of the database to connect to.</param>
        /// <param name="dataAccessProviderFactory">An `OdbcFactory` or `OleDbFactory` object to be used for all
        /// data access operations by the Jet connection.</param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder UseJet(
            this DbContextOptionsBuilder optionsBuilder,
            string? fileNameOrConnectionString,
            DbProviderFactory dataAccessProviderFactory,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
        {
            return UseJetCore(optionsBuilder, fileNameOrConnectionString, dataAccessProviderFactory, null, jetOptionsAction);
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
            this DbContextOptionsBuilder optionsBuilder,
            DbConnection connection,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
            => UseJet(optionsBuilder, connection, false, jetOptionsAction);

        // Note: Decision made to use DbConnection not SqlConnection: Issue #772
        /// <summary>
        ///     Configures the context to connect to a Jet database.
        /// </summary>
        /// <param name="optionsBuilder">The builder being used to configure the context.</param>
        /// <param name="connection">
        ///     An existing <see cref="DbConnection" /> to be used to connect to the database. If the connection is
        ///     in the open state then EF will not open or close the connection. If the connection is in the closed
        ///     state then EF will open and close the connection as needed.
        /// </param>
        /// <param name="contextOwnsConnection">
        ///     If <see langword="true" />, then EF will take ownership of the connection and will
        ///     dispose it in the same way it would dispose a connection created by EF. If <see langword="false" />, then the caller still
        ///     owns the connection and is responsible for its disposal.
        /// </param>
        /// <param name="jetOptionsAction">An optional action to allow additional SQL Server specific configuration.</param>
        /// <returns>The options builder so that further configuration can be chained.</returns>
        public static DbContextOptionsBuilder UseJet(
            this DbContextOptionsBuilder optionsBuilder,
            DbConnection connection,
            bool contextOwnsConnection,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
        {
            Check.NotNull(connection, nameof(connection));

            var jetConnection = connection as JetConnection;
            if (jetConnection == null)
            {
                throw new ArgumentException($"The {nameof(connection)} parameter must be of type {nameof(JetConnection)}.");
            }

            if (jetConnection.DataAccessProviderFactory == null)
            {
                var fileNameOrConnectionString = jetConnection.ConnectionString;
                DataAccessProviderType dataAccessProviderType;

                if (JetConnection.IsConnectionString(fileNameOrConnectionString))
                {
                    dataAccessProviderType = JetConnection.GetDataAccessProviderType(fileNameOrConnectionString);
                }
                else if (JetConnection.IsFileName(fileNameOrConnectionString))
                {
                    dataAccessProviderType = JetConfiguration.DefaultDataAccessProviderType;
                }
                else
                {
                    throw new ArgumentException($"The data access provider type could not be inferred from the connections {nameof(JetConnection.DataAccessProviderFactory)} or {nameof(JetConnection.ConnectionString)} property and the {nameof(JetConnection.ConnectionString)} property is not a valid file name either.");
                }

                jetConnection.DataAccessProviderFactory = JetFactory.Instance.GetDataAccessProviderFactory(dataAccessProviderType);
                jetConnection.Freeze();
            }

            var extension = (JetOptionsExtension)GetOrCreateExtension(optionsBuilder)
                .WithConnection(connection, contextOwnsConnection);
            extension = extension.WithDataAccessProviderFactory(jetConnection.DataAccessProviderFactory);
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

            return ApplyConfiguration(optionsBuilder, jetOptionsAction);
        }

        /// <summary>
        ///     Configures the context to connect to a Jet database, but without initially setting any
        ///     <see cref="DbConnection" /> or connection string.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The connection or connection string must be set before the <see cref="DbContext" /> is used to connect
        ///         to a database. Set a connection using <see cref="RelationalDatabaseFacadeExtensions.SetDbConnection" />.
        ///         Set a connection string using <see cref="RelationalDatabaseFacadeExtensions.SetConnectionString" />.
        ///     </para>
        ///     <para>
        ///         See <see href="https://aka.ms/efcore-docs-dbcontext-options">Using DbContextOptions</see>
        ///     </para>
        /// </remarks>
        /// <param name="optionsBuilder">The builder being used to configure the context.</param>
        /// <param name="jetOptionsAction">An optional action to allow additional SQL Server specific configuration.</param>
        /// <returns>The options builder so that further configuration can be chained.</returns>
        public static DbContextOptionsBuilder<TContext> UseJet<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
            where TContext : DbContext
            => (DbContextOptionsBuilder<TContext>)UseJet(
                (DbContextOptionsBuilder)optionsBuilder, jetOptionsAction);

        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="fileNameOrConnectionString"> The file name or connection string of the database to connect to.
        /// If just a file name is supplied, the default data access provider type as defined by
        /// `JetConfiguration.DefaultDataAccessProviderType` is being used. If a connection string is supplied, the
        /// underlying data access provider (ODBC or OLE DB) will be inferred from the style of the connection string.
        /// In case the connection string does not specify an Access driver (ODBC) or ACE/Jet provider (OLE DB), the
        /// highest version of all compatible installed ones is being used. </param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder<TContext> UseJet<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder,
            string? fileNameOrConnectionString,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
            where TContext : DbContext =>
            (DbContextOptionsBuilder<TContext>)UseJet((DbContextOptionsBuilder)optionsBuilder, fileNameOrConnectionString, jetOptionsAction);

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
            this DbContextOptionsBuilder<TContext> optionsBuilder,
            DbConnection connection,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
            where TContext : DbContext
        {
            Check.NotNull(optionsBuilder, nameof(optionsBuilder));
            Check.NotNull(connection, nameof(connection));

            return (DbContextOptionsBuilder<TContext>)UseJet(
                (DbContextOptionsBuilder)optionsBuilder, connection, jetOptionsAction);
        }

        // Note: Decision made to use DbConnection not SqlConnection: Issue #772
        /// <summary>
        ///     Configures the context to connect to a Microsoft SJET database.
        /// </summary>
        /// <remarks>
        ///     See <see href="https://aka.ms/efcore-docs-dbcontext-options">Using DbContextOptions</see>, and
        ///     <see href="https://aka.ms/efcore-docs-sqlserver">Accessing SQL Server and Azure SQL databases with EF Core</see>
        ///     for more information and examples.
        /// </remarks>
        /// <typeparam name="TContext">The type of context to be configured.</typeparam>
        /// <param name="optionsBuilder">The builder being used to configure the context.</param>
        /// <param name="connection">
        ///     An existing <see cref="DbConnection" /> to be used to connect to the database. If the connection is
        ///     in the open state then EF will not open or close the connection. If the connection is in the closed
        ///     state then EF will open and close the connection as needed.
        /// </param>
        /// <param name="contextOwnsConnection">
        ///     If <see langword="true" />, then EF will take ownership of the connection and will
        ///     dispose it in the same way it would dispose a connection created by EF. If <see langword="false" />, then the caller still
        ///     owns the connection and is responsible for its disposal.
        /// </param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns>The options builder so that further configuration can be chained.</returns>
        public static DbContextOptionsBuilder<TContext> UseJet<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder,
            DbConnection connection,
            bool contextOwnsConnection,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
            where TContext : DbContext
            => (DbContextOptionsBuilder<TContext>)UseJet(
                (DbContextOptionsBuilder)optionsBuilder, connection, contextOwnsConnection, jetOptionsAction);

        #region Connection String

        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="fileNameOrConnectionString"> The file name or connection string of the database to connect to.</param>
        /// <param name="dataAccessProviderType">The type of the data access provider (`Odbc` or `OleDb`) to be used for all
        /// data access operations by the Jet connection.</param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder<TContext> UseJet<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder,
            string? fileNameOrConnectionString,
            DataAccessProviderType dataAccessProviderType,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
            where TContext : DbContext
        {
            return (DbContextOptionsBuilder<TContext>)UseJet((DbContextOptionsBuilder)optionsBuilder, fileNameOrConnectionString, dataAccessProviderType, jetOptionsAction);
        }

        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="fileNameOrConnectionString"> The file name or connection string of the database to connect to.</param>
        /// <param name="dataAccessProviderType">The type of the data access provider (`Odbc` or `OleDb`) to be used for all
        /// data access operations by the Jet connection.</param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder UseJet(
            this DbContextOptionsBuilder optionsBuilder,
            string? fileNameOrConnectionString,
            DataAccessProviderType dataAccessProviderType,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
        {
            return UseJetCore(optionsBuilder, fileNameOrConnectionString, null, dataAccessProviderType, jetOptionsAction);
        }

        internal static DbContextOptionsBuilder UseJetWithoutPredefinedDataAccessProvider(
            this DbContextOptionsBuilder optionsBuilder,
            string? fileNameOrConnectionString,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
            => UseJetCore(optionsBuilder, fileNameOrConnectionString, null, null, jetOptionsAction);

        private static DbContextOptionsBuilder UseJetCore(
            DbContextOptionsBuilder optionsBuilder,
            string? fileNameOrConnectionString,
            DbProviderFactory? dataAccessProviderFactory,
            DataAccessProviderType? dataAccessProviderType,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction)
        {
            if (dataAccessProviderFactory == null && dataAccessProviderType == null)
            {
                if (JetConnection.IsConnectionString(fileNameOrConnectionString))
                {
                    dataAccessProviderType = JetConnection.GetDataAccessProviderType(fileNameOrConnectionString);
                }
                else if (JetConnection.IsFileName(fileNameOrConnectionString))
                {
                    dataAccessProviderType = JetConfiguration.DefaultDataAccessProviderType;
                }
                else
                {
                    throw new ArgumentException($"Either {nameof(dataAccessProviderFactory)} or {nameof(dataAccessProviderType)} must not be null, or a file name must be specified for {nameof(fileNameOrConnectionString)}.");
                }
            }

            var extension = (JetOptionsExtension)GetOrCreateExtension(optionsBuilder)
                .WithConnectionString(fileNameOrConnectionString);

            extension = extension.WithDataAccessProviderFactory(
                dataAccessProviderFactory ?? JetFactory.Instance.GetDataAccessProviderFactory(dataAccessProviderType!.Value));

            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

            return ApplyConfiguration(optionsBuilder, jetOptionsAction);
        }

        #endregion

        private static JetOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.Options.FindExtension<JetOptionsExtension>()
               ?? new JetOptionsExtension();

        private static DbContextOptionsBuilder ApplyConfiguration(
            DbContextOptionsBuilder optionsBuilder,
            Action<JetDbContextOptionsBuilder>? jetOptionsAction)
        {
            ConfigureWarnings(optionsBuilder);

            jetOptionsAction?.Invoke(new JetDbContextOptionsBuilder(optionsBuilder));

            var extension = (JetOptionsExtension)GetOrCreateExtension(optionsBuilder);
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

            return optionsBuilder;
        }

        private static void ConfigureWarnings(DbContextOptionsBuilder optionsBuilder)
        {
            var coreOptionsExtension
                = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()
                  ?? new CoreOptionsExtension();

            coreOptionsExtension = RelationalOptionsExtension.WithDefaultWarningConfiguration(coreOptionsExtension);

            coreOptionsExtension = coreOptionsExtension.WithWarningsConfiguration(
                coreOptionsExtension.WarningsConfiguration.TryWithExplicit(
                    RelationalEventId.AmbientTransactionWarning, WarningBehavior.Throw));

            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(coreOptionsExtension);
        }
    }
}