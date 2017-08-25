// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EntityFrameworkCore.Jet.Utilities;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet
{
    /// <summary>
    ///     Jet specific extension methods for <see cref="EntityTypeBuilder" />.
    /// </summary>
    public static class JetEntityTypeBuilderExtensions
    {
        /// <summary>
        ///     Configures the table that the entity maps to when targeting Jet as memory-optimized.
        /// </summary>
        /// <param name="entityTypeBuilder"> The builder for the entity type being configured. </param>
        /// <param name="memoryOptimized"> A value indicating whether the table is memory-optimized. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static EntityTypeBuilder ForJetIsMemoryOptimized(
            [NotNull] this EntityTypeBuilder entityTypeBuilder, bool memoryOptimized = true)
        {
            Check.NotNull(entityTypeBuilder, nameof(entityTypeBuilder));

            entityTypeBuilder.Metadata.Jet().IsMemoryOptimized = memoryOptimized;

            return entityTypeBuilder;
        }

        /// <summary>
        ///     Configures the table that the entity maps to when targeting Jet as memory-optimized.
        /// </summary>
        /// <typeparam name="TEntity"> The entity type being configured. </typeparam>
        /// <param name="entityTypeBuilder"> The builder for the entity type being configured. </param>
        /// <param name="memoryOptimized"> A value indicating whether the table is memory-optimized. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static EntityTypeBuilder<TEntity> ForJetIsMemoryOptimized<TEntity>(
            [NotNull] this EntityTypeBuilder<TEntity> entityTypeBuilder, bool memoryOptimized = true)
            where TEntity : class
            => (EntityTypeBuilder<TEntity>)ForJetIsMemoryOptimized((EntityTypeBuilder)entityTypeBuilder, memoryOptimized);
    }
}
