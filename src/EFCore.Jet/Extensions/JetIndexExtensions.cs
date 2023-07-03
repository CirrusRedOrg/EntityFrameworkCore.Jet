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
        ///     Returns included property names, or <c>null</c> if they have not been specified.
        /// </summary>
        /// <param name="index"> The index. </param>
        /// <returns> The included property names, or <c>null</c> if they have not been specified. </returns>
        public static IReadOnlyList<string>? GetJetIncludeProperties([NotNull] this IReadOnlyIndex index)
            => (index is RuntimeIndex)
                ? throw new InvalidOperationException(CoreStrings.RuntimeModelMissingData)
                : (string[]?)index[JetAnnotationNames.Include];

        /// <summary>
        ///     Sets included property names.
        /// </summary>
        /// <param name="index"> The index. </param>
        /// <param name="properties"> The value to set. </param>
        public static void SetJetIncludeProperties([NotNull] this IMutableIndex index, [NotNull] IReadOnlyList<string> properties)
            => index.SetOrRemoveAnnotation(
                JetAnnotationNames.Include,
                properties);

        /// <summary>
        ///     Sets included property names.
        /// </summary>
        /// <param name="index"> The index. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <param name="properties"> The value to set. </param>
        public static void SetJetIncludeProperties(
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
        public static ConfigurationSource? GetJetIncludePropertiesConfigurationSource([NotNull] this IConventionIndex index)
            => index.FindAnnotation(JetAnnotationNames.Include)?.GetConfigurationSource();
    }
}