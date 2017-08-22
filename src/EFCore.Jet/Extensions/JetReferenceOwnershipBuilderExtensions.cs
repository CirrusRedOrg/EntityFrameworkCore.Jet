// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EntityFrameworkCore.Jet.Utilities;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet
{
    /// <summary>
    ///     SQL Server specific extension methods for <see cref="ReferenceOwnershipBuilder" />.
    /// </summary>
    public static class JetReferenceOwnershipBuilderExtensions
    {
        /// <summary>
        ///     Configures the table that the entity maps to when targeting SQL Server as memory-optimized.
        /// </summary>
        /// <param name="referenceOwnershipBuilder"> The builder for the entity type being configured. </param>
        /// <param name="memoryOptimized"> A value indicating whether the table is memory-optimized. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static ReferenceOwnershipBuilder ForJetIsMemoryOptimized(
            [NotNull] this ReferenceOwnershipBuilder referenceOwnershipBuilder, bool memoryOptimized = true)
        {
            Check.NotNull(referenceOwnershipBuilder, nameof(referenceOwnershipBuilder));

            referenceOwnershipBuilder.OwnedEntityType.Jet().IsMemoryOptimized = memoryOptimized;

            return referenceOwnershipBuilder;
        }

        /// <summary>
        ///     Configures the table that the entity maps to when targeting SQL Server as memory-optimized.
        /// </summary>
        /// <typeparam name="TEntity"> The entity type being configured. </typeparam>
        /// <typeparam name="TRelatedEntity"> The entity type that this relationship targets. </typeparam>
        /// <param name="referenceOwnershipBuilder"> The builder for the entity type being configured. </param>
        /// <param name="memoryOptimized"> A value indicating whether the table is memory-optimized. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static ReferenceOwnershipBuilder<TEntity, TRelatedEntity> ForJetIsMemoryOptimized<TEntity, TRelatedEntity>(
            [NotNull] this ReferenceOwnershipBuilder<TEntity, TRelatedEntity> referenceOwnershipBuilder, bool memoryOptimized = true)
            where TEntity : class
            where TRelatedEntity : class
            => (ReferenceOwnershipBuilder<TEntity, TRelatedEntity>)ForJetIsMemoryOptimized((ReferenceOwnershipBuilder)referenceOwnershipBuilder, memoryOptimized);
    }
}
