// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Infrastructure.Internal;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    ///     Jet specific extension methods for <see cref="DbContext.Database" />.
    /// </summary>
    public static class JetDatabaseFacadeExtensions
    {
        /// <summary>
        ///     <para>
        ///         Returns true if the database provider currently in use is the Jet provider.
        ///     </para>
        ///     <para>
        ///         This method can only be used after the <see cref="DbContext" /> has been configured because
        ///         it is only then that the provider is known. This means that this method cannot be used
        ///         in <see cref="DbContext.OnConfiguring" /> because this is where application code sets the
        ///         provider to use as part of configuring the context.
        ///     </para>
        /// </summary>
        /// <param name="database"> The facade from <see cref="DbContext.Database" />. </param>
        /// <returns> True if Jet is being used; false otherwise. </returns>
        public static bool IsJet(this DatabaseFacade database)
            => database.ProviderName == typeof(JetOptionsExtension).Assembly.GetName().Name;
    }
}
