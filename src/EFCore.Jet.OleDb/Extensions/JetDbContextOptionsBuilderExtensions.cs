// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.Jet.Data;
using System.Data.OleDb;
using EntityFrameworkCore.Jet.Infrastructure;
using JetBrains.Annotations;
using EntityFrameworkCore.Jet.Utilities;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    ///     Jet specific OLE DB extension methods for <see cref="DbContextOptionsBuilder" />.
    /// </summary>
    public static class JetOleDbDbContextOptionsBuilderExtensions
    {
        #region Connection String
        
        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database using OLE DB.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="fileNameOrConnectionString"> The file name or OLE DB connection string of the database to connect
        /// to. In case the connection string does not specify an Jet/ACE provider (OLE DB), the highest version of all
        /// compatible installed ones is being used. </param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder<TContext> UseJetOleDb<TContext>(
            [NotNull] this DbContextOptionsBuilder<TContext> optionsBuilder,
            [NotNull] string fileNameOrConnectionString,
            [CanBeNull] Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
            where TContext : DbContext
            => optionsBuilder.UseJet(fileNameOrConnectionString, DataAccessProviderType.OleDb, jetOptionsAction);

        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database using OLE DB.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="fileNameOrConnectionString"> The file name or OLE DB connection string of the database to connect
        /// to. In case the connection string does not specify an Jet/ACE provider (OLE DB), the highest version of all
        /// compatible installed ones is being used. </param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder UseJetOleDb(
            [NotNull] this DbContextOptionsBuilder optionsBuilder,
            [NotNull] string fileNameOrConnectionString,
            [CanBeNull] Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
            => optionsBuilder.UseJet(fileNameOrConnectionString, DataAccessProviderType.OleDb, jetOptionsAction);

        #endregion
        
        #region Connection
        
        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database using OLE DB.
        /// </summary>
        /// <typeparam name="TContext"> The type of context to be configured. </typeparam>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="connection">
        ///     An existing <see cref="OleDbConnection" /> to be used to connect to the database. If the connection is in
        ///     the open state then EF will not open or close the connection. If the connection is in the closed state
        ///     then EF will open and close the connection as needed.
        /// </param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder<TContext> UseJetOleDb<TContext>(
            [NotNull] this DbContextOptionsBuilder<TContext> optionsBuilder,
            [NotNull] OleDbConnection connection,
            [CanBeNull] Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
            where TContext : DbContext
        {
            Check.NotNull(optionsBuilder, nameof(optionsBuilder));
            Check.NotNull(connection, nameof(connection));

            return optionsBuilder.UseJet(connection, jetOptionsAction);
        }

        /// <summary>
        ///     Configures the context to connect to a Microsoft Jet database using OLE DB.
        /// </summary>
        /// <param name="optionsBuilder"> The builder being used to configure the context. </param>
        /// <param name="connection">
        ///     An existing <see cref="OleDbConnection" /> to be used to connect to the database. If the connection is in
        ///     the open state then EF will not open or close the connection. If the connection is in the closed state
        ///     then EF will open and close the connection as needed.
        /// </param>
        /// <param name="jetOptionsAction">An optional action to allow additional Jet specific configuration.</param>
        /// <returns> The options builder so that further configuration can be chained. </returns>
        public static DbContextOptionsBuilder UseJetOleDb(
            [NotNull] this DbContextOptionsBuilder optionsBuilder,
            [NotNull] OleDbConnection connection,
            [CanBeNull] Action<JetDbContextOptionsBuilder>? jetOptionsAction = null)
        {
            Check.NotNull(optionsBuilder, nameof(optionsBuilder));
            Check.NotNull(connection, nameof(connection));

            return optionsBuilder.UseJet(connection, jetOptionsAction);
        }
        
        #endregion
    }
}