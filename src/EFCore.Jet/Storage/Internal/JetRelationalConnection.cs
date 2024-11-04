// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.Infrastructure.Internal;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    /// <remarks>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </remarks>
    public class JetRelationalConnection(RelationalConnectionDependencies dependencies) : RelationalConnection(dependencies), IJetRelationalConnection
    {
        private JetOptionsExtension _jetOptionsExtension = dependencies.ContextOptions.FindExtension<JetOptionsExtension>() ?? new JetOptionsExtension();

        // Compensate for slow Jet database creation
        internal const int DefaultMasterConnectionCommandTimeout = 60;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override DbConnection CreateDbConnection()
        {
            var connection = (JetConnection)JetFactory.Instance.CreateConnection();
            connection.DataAccessProviderFactory = _jetOptionsExtension.DataAccessProviderFactory!;
            connection.ConnectionString = ConnectionString!;

            return connection;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IJetRelationalConnection CreateEmptyConnection()
        {
            var connection = (JetConnection)JetFactory.Instance.CreateConnection();
            connection.DataAccessProviderFactory = _jetOptionsExtension.DataAccessProviderFactory!;
            connection.IsEmpty = true;

            var contextOptions = new DbContextOptionsBuilder()
                .UseJet(connection, b => b.CommandTimeout(CommandTimeout ?? DefaultMasterConnectionCommandTimeout))
                .Options;

            return new JetRelationalConnection(Dependencies with { ContextOptions = contextOptions });
        }

        /// <summary>
        ///     Indicates whether the store connection supports ambient transactions
        /// </summary>
        protected override bool SupportsAmbientTransactions => false;
    }
}