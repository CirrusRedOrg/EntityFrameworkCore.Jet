// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Infrastructure.Internal;
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
            
            ConnectionString = jetOptions.Connection?.ConnectionString ?? jetOptions.ConnectionString;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual void Validate(IDbContextOptions options)
        {
            /*
            var jetOptions = options.FindExtension<JetOptionsExtension>() ?? new JetOptionsExtension();

            if (RowNumberPagingEnabled != (jetOptions.RowNumberPaging ?? false))
            {
                throw new InvalidOperationException(
                    CoreStrings.SingletonOptionChanged(
                        nameof(JetDbContextOptionsBuilder.UseRowNumberForPaging),
                        nameof(DbContextOptionsBuilder.UseInternalServiceProvider)));
            }
            */
        }
        
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        // public virtual bool RowNumberPagingEnabled { get; private set; }

        public virtual string ConnectionString { get; private set; }
    }
}
