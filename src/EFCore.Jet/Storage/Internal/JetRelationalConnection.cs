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
    public class JetRelationalConnection : RelationalConnection, IJetRelationalConnection
    {
        // Compensate for slow Jet database creation
        internal const int DefaultMasterConnectionCommandTimeout = 60;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetRelationalConnection([NotNull] RelationalConnectionDependencies dependencies)
            : base(dependencies)
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override DbConnection CreateDbConnection() => new System.Data.Jet.JetConnection(ConnectionString);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IJetRelationalConnection CreateEmptyConnection()
        {
            var connection = new System.Data.Jet.JetConnection();
            connection.IsEmpty = true;
            var contextOptions = new DbContextOptionsBuilder()
                .UseJet(connection)
                .Options;

            return new JetRelationalConnection(Dependencies.With(contextOptions));
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override bool IsMultipleActiveResultSetsEnabled => false;
    }
}
