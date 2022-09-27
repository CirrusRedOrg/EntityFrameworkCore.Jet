using System;
using System.Collections.Generic;
using EntityFrameworkCore.Jet.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    ///     Extension methods for <see cref="IIndex" /> for Jet-specific metadata.
    /// </summary>
    public static class JetIndexExtensions
    {
        /// <summary>
        ///     Returns a value indicating whether the index is clustered.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns><see langword="true" /> if the index is clustered.</returns>
        public static bool? IsClustered(this IReadOnlyIndex index)
            => (index is RuntimeIndex)
                ? throw new InvalidOperationException(CoreStrings.RuntimeModelMissingData)
                : (bool?)index[JetAnnotationNames.Clustered];

        /// <summary>
        ///     Returns a value indicating whether the index is clustered.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="storeObject">The identifier of the store object.</param>
        /// <returns><see langword="true" /> if the index is clustered.</returns>
        public static bool? IsClustered(this IReadOnlyIndex index, in StoreObjectIdentifier storeObject)
        {
            if (index is RuntimeIndex)
            {
                throw new InvalidOperationException(CoreStrings.RuntimeModelMissingData);
            }

            var annotation = index.FindAnnotation(JetAnnotationNames.Clustered);
            if (annotation != null)
            {
                return (bool?)annotation.Value;
            }

            var sharedTableRootIndex = index.FindSharedObjectRootIndex(storeObject);
            return sharedTableRootIndex?.IsClustered(storeObject);
        }

        /// <summary>
        ///     Sets a value indicating whether the index is clustered.
        /// </summary>
        /// <param name="value">The value to set.</param>
        /// <param name="index">The index.</param>
        public static void SetIsClustered(this IMutableIndex index, bool? value)
            => index.SetAnnotation(
                JetAnnotationNames.Clustered,
                value);

        /// <summary>
        ///     Sets a value indicating whether the index is clustered.
        /// </summary>
        /// <param name="value">The value to set.</param>
        /// <param name="index">The index.</param>
        /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
        /// <returns>The configured value.</returns>
        public static bool? SetIsClustered(
            this IConventionIndex index,
            bool? value,
            bool fromDataAnnotation = false)
            => (bool?)index.SetAnnotation(
                JetAnnotationNames.Clustered,
                value,
                fromDataAnnotation)?.Value;

        /// <summary>
        ///     Returns the <see cref="ConfigurationSource" /> for whether the index is clustered.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <returns>The <see cref="ConfigurationSource" /> for whether the index is clustered.</returns>
        public static ConfigurationSource? GetIsClusteredConfigurationSource(this IConventionIndex property)
            => property.FindAnnotation(JetAnnotationNames.Clustered)?.GetConfigurationSource();

        /// <summary>
        ///     Returns included property names, or <c>null</c> if they have not been specified.
        /// </summary>
        /// <param name="index"> The index. </param>
        /// <returns> The included property names, or <c>null</c> if they have not been specified. </returns>
        public static IReadOnlyList<string> GetIncludeProperties([NotNull] this IIndex index)
            => (string[])index[JetAnnotationNames.Include];

        /// <summary>
        ///     Sets included property names.
        /// </summary>
        /// <param name="index"> The index. </param>
        /// <param name="properties"> The value to set. </param>
        public static void SetIncludeProperties([NotNull] this IMutableIndex index, [NotNull] IReadOnlyList<string> properties)
            => index.SetOrRemoveAnnotation(
                JetAnnotationNames.Include,
                properties);

        /// <summary>
        ///     Sets included property names.
        /// </summary>
        /// <param name="index"> The index. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <param name="properties"> The value to set. </param>
        public static void SetIncludeProperties(
            [NotNull] this IConventionIndex index, [NotNull] IReadOnlyList<string> properties, bool fromDataAnnotation = false)
            => index.SetOrRemoveAnnotation(
                JetAnnotationNames.Include,
                properties,
                fromDataAnnotation);

        /// <summary>
        ///     Returns the <see cref="ConfigurationSource" /> for the included property names.
        /// </summary>
        /// <param name="index"> The index. </param>
        /// <returns> The <see cref="ConfigurationSource" /> for the included property names. </returns>
        public static ConfigurationSource? GetIncludePropertiesConfigurationSource([NotNull] this IConventionIndex index)
            => index.FindAnnotation(JetAnnotationNames.Include)?.GetConfigurationSource();
    }
}