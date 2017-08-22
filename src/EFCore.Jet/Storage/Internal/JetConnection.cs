// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data.Common;
using System.Data.Jet;
using System.Data.OleDb;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetConnection : RelationalConnection, IJetConnection
    {
        // Compensate for slow SQL Server database creation
        internal const int DefaultMasterConnectionCommandTimeout = 60;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetConnection([NotNull] RelationalConnectionDependencies dependencies)
            : base(dependencies)
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override DbConnection CreateDbConnection() => new System.Data.Jet.JetConnection(ConnectionString);

        // TODO use clone connection method once implemented see #1406
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IJetConnection CreateMasterConnection()
        {
            var connectionStringBuilder = new OleDbConnectionStringBuilder() { };
            connectionStringBuilder.Remove("AttachDBFilename");

            var contextOptions = new DbContextOptionsBuilder()
                .UseJet(
                    connectionStringBuilder.ConnectionString,
                    b => b.CommandTimeout(CommandTimeout ?? DefaultMasterConnectionCommandTimeout))
                .Options;

            return new JetConnection(Dependencies.With(contextOptions));
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override bool IsMultipleActiveResultSetsEnabled => false;
    }
}
