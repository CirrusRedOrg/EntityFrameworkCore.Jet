// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EntityFrameworkCore.Jet.Utilities;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet
{
    /// <summary>
    ///     SQL Server specific extension methods for <see cref="KeyBuilder" />.
    /// </summary>
    public static class JetKeyBuilderExtensions
    {
        /// <summary>
        ///     Configures whether the key is clustered when targeting SQL Server.
        /// </summary>
        /// <param name="keyBuilder"> The builder for the key being configured. </param>
        /// <param name="clustered"> A value indicating whether the key is clustered. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static KeyBuilder ForJetIsClustered([NotNull] this KeyBuilder keyBuilder, bool clustered = true)
        {
            Check.NotNull(keyBuilder, nameof(keyBuilder));

            keyBuilder.Metadata.Jet().IsClustered = clustered;

            return keyBuilder;
        }
    }
}
