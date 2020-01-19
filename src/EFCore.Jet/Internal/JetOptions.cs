// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
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
            ConnectionString = jetOptions.Connection?.ConnectionString ?? jetOptions.ConnectionString;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual void Validate(IDbContextOptions options)
        {
        }

        public virtual string ConnectionString { get; private set; }

        protected bool Equals(JetOptions other)
        {
            return ConnectionString == other.ConnectionString;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != this.GetType())
                return false;
            return Equals((JetOptions) obj);
        }

        public override int GetHashCode()
        {
            return (ConnectionString != null ? ConnectionString.GetHashCode() : 0);
        }
    }
}
