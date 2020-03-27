// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Jet;
using System.Linq;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.Jet.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetOptions : IJetOptions
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual void Initialize(IDbContextOptions options)
        {
            var jetOptions = options.FindExtension<JetOptionsExtension>() ?? new JetOptionsExtension();

            // RowNumberPagingEnabled = jetOptions.RowNumberPaging ?? false;
            
            DataAccessType = GetDataAccessTypeFromOptions(jetOptions);
            ConnectionString = jetOptions.Connection?.ConnectionString ?? jetOptions.ConnectionString;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual void Validate(IDbContextOptions options)
        {
            var jetOptions = options.FindExtension<JetOptionsExtension>() ?? new JetOptionsExtension();

            /*
            if (RowNumberPagingEnabled != (jetOptions.RowNumberPaging ?? false))
            {
                throw new InvalidOperationException(
                    CoreStrings.SingletonOptionChanged(
                        nameof(JetDbContextOptionsBuilder.UseRowNumberForPaging),
                        nameof(DbContextOptionsBuilder.UseInternalServiceProvider)));
            }
            */

            if (DataAccessType != GetDataAccessTypeFromOptions(jetOptions))
            {
                throw new InvalidOperationException(
                    CoreStrings.SingletonOptionChanged(
                        nameof(JetOptionsExtension.DataAccessProviderFactory),
                        nameof(DbContextOptionsBuilder.UseInternalServiceProvider)));
            }
        }
        
        private static DataAccessType GetDataAccessTypeFromOptions(JetOptionsExtension jetOptions)
            => jetOptions.DataAccessProviderFactory
        private static DataAccessProviderType GetDataAccessTypeFromOptions(JetOptionsExtension jetOptions)
        {
            if (jetOptions.DataAccessProviderFactory == null)
            {
                throw new InvalidOperationException(JetStrings.DataAccessProviderFactory);
            }
            
            if (jetOptions.DataAccessProviderFactory
                   .GetType()
                   .GetTypesInHierarchy()
                .Any(
                       t => string.Equals(
                           t.FullName,
                           "System.Data.OleDb.OleDbFactory",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return DataAccessProviderType.OleDb;
            }

            if (jetOptions.DataAccessProviderFactory
                .GetType()
                .GetTypesInHierarchy()
                .Any(
                    t => string.Equals(
                        t.FullName,
                        "System.Data.Odbc.OdbcFactory",
                        StringComparison.OrdinalIgnoreCase)))

            {
                return DataAccessProviderType.Odbc;
            }
            
            throw new InvalidOperationException("The JetConnection.DataAccessProviderFactory property needs to be set to an object of type OdbcFactory or OleDbFactory.");
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        // public virtual bool RowNumberPagingEnabled { get; private set; }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual DataAccessType DataAccessType { get; private set; }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual string ConnectionString { get; private set; }
    }
}
