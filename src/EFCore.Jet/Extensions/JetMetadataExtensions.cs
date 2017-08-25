// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Metadata;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using EntityFrameworkCore.Jet.Utilities;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet
{
    /// <summary>
    ///     Jet specific extension methods for metadata.
    /// </summary>
    public static class JetMetadataExtensions
    {
        /// <summary>
        ///     Gets the Jet specific metadata for a property.
        /// </summary>
        /// <param name="property"> The property to get metadata for. </param>
        /// <returns> The Jet specific metadata for the property. </returns>
        public static JetPropertyAnnotations Jet([NotNull] this IMutableProperty property)
            => (JetPropertyAnnotations)Jet((IProperty)property);

        /// <summary>
        ///     Gets the Jet specific metadata for a property.
        /// </summary>
        /// <param name="property"> The property to get metadata for. </param>
        /// <returns> The Jet specific metadata for the property. </returns>
        public static IJetPropertyAnnotations Jet([NotNull] this IProperty property)
            => new JetPropertyAnnotations(Check.NotNull(property, nameof(property)));

        /// <summary>
        ///     Gets the Jet specific metadata for an entity.
        /// </summary>
        /// <param name="entityType"> The entity to get metadata for. </param>
        /// <returns> The Jet specific metadata for the entity. </returns>
        public static JetEntityTypeAnnotations Jet([NotNull] this IMutableEntityType entityType)
            => (JetEntityTypeAnnotations)Jet((IEntityType)entityType);

        /// <summary>
        ///     Gets the Jet specific metadata for an entity.
        /// </summary>
        /// <param name="entityType"> The entity to get metadata for. </param>
        /// <returns> The Jet specific metadata for the entity. </returns>
        public static IJetEntityTypeAnnotations Jet([NotNull] this IEntityType entityType)
            => new JetEntityTypeAnnotations(Check.NotNull(entityType, nameof(entityType)));

        /// <summary>
        ///     Gets the Jet specific metadata for a key.
        /// </summary>
        /// <param name="key"> The key to get metadata for. </param>
        /// <returns> The Jet specific metadata for the key. </returns>
        public static JetKeyAnnotations Jet([NotNull] this IMutableKey key)
            => (JetKeyAnnotations)Jet((IKey)key);

        /// <summary>
        ///     Gets the Jet specific metadata for a key.
        /// </summary>
        /// <param name="key"> The key to get metadata for. </param>
        /// <returns> The Jet specific metadata for the key. </returns>
        public static IJetKeyAnnotations Jet([NotNull] this IKey key)
            => new JetKeyAnnotations(Check.NotNull(key, nameof(key)));

        /// <summary>
        ///     Gets the Jet specific metadata for an index.
        /// </summary>
        /// <param name="index"> The index to get metadata for. </param>
        /// <returns> The Jet specific metadata for the index. </returns>
        public static JetIndexAnnotations Jet([NotNull] this IMutableIndex index)
            => (JetIndexAnnotations)Jet((IIndex)index);

        /// <summary>
        ///     Gets the Jet specific metadata for an index.
        /// </summary>
        /// <param name="index"> The index to get metadata for. </param>
        /// <returns> The Jet specific metadata for the index. </returns>
        public static IJetIndexAnnotations Jet([NotNull] this IIndex index)
            => new JetIndexAnnotations(Check.NotNull(index, nameof(index)));

        /// <summary>
        ///     Gets the Jet specific metadata for a model.
        /// </summary>
        /// <param name="model"> The model to get metadata for. </param>
        /// <returns> The Jet specific metadata for the model. </returns>
        public static JetModelAnnotations Jet([NotNull] this IMutableModel model)
            => (JetModelAnnotations)Jet((IModel)model);

        /// <summary>
        ///     Gets the Jet specific metadata for a model.
        /// </summary>
        /// <param name="model"> The model to get metadata for. </param>
        /// <returns> The Jet specific metadata for the model. </returns>
        public static IJetModelAnnotations Jet([NotNull] this IModel model)
            => new JetModelAnnotations(Check.NotNull(model, nameof(model)));
    }
}
