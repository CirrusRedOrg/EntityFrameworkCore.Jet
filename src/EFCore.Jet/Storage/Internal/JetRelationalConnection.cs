// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using EntityFrameworkCore.Jet.Internal;
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
        private JetOptionsExtension _jetOptionsExtension;

        // Compensate for slow Jet database creation
        internal const int DefaultMasterConnectionCommandTimeout = 60;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetRelationalConnection([NotNull] RelationalConnectionDependencies dependencies)
            : base(dependencies)
        {
            _jetOptionsExtension = dependencies.ContextOptions.FindExtension<JetOptionsExtension>() ?? new JetOptionsExtension();

            if (_jetOptionsExtension.DataAccessProviderFactory == null &&
                ((JetConnection) DbConnection)?.DataAccessProviderFactory == null)
            {
                throw new InvalidOperationException(JetStrings.DataAccessProviderFactory);
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override DbConnection CreateDbConnection()
        {
            var connection = (JetConnection) JetFactory.Instance.CreateConnection();
            connection.DataAccessProviderFactory = _jetOptionsExtension.DataAccessProviderFactory;
            connection.ConnectionString = ConnectionString;

            return connection;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual IJetRelationalConnection CreateEmptyConnection()
        {
            var connection = (JetConnection) JetFactory.Instance.CreateConnection();
            connection.DataAccessProviderFactory = _jetOptionsExtension.DataAccessProviderFactory;
            connection.IsEmpty = true;

            var contextOptions = new DbContextOptionsBuilder()
                .UseJet(connection, b => b.CommandTimeout(CommandTimeout ?? DefaultMasterConnectionCommandTimeout))
                .Options;

            return new JetRelationalConnection(Dependencies.With(contextOptions));
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override bool IsMultipleActiveResultSetsEnabled => false;

        /// <summary>
        ///     Indicates whether the store connection supports ambient transactions
        /// </summary>
        protected override bool SupportsAmbientTransactions => false;
    }
}