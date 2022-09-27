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
    ///     Key extension methods for SQL Server-specific metadata.
    /// </summary>
    public static class JetKeyExtensions
    {
        /// <summary>
        ///     Returns a value indicating whether the key is clustered.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns><see langword="true" /> if the key is clustered.</returns>
        public static bool? IsClustered(this IReadOnlyKey key)
            => (key is RuntimeKey)
                ? throw new InvalidOperationException(CoreStrings.RuntimeModelMissingData)
                : (bool?)key[JetAnnotationNames.Clustered];

        /// <summary>
        ///     Returns a value indicating whether the key is clustered.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="storeObject">The identifier of the store object.</param>
        /// <returns><see langword="true" /> if the key is clustered.</returns>
        public static bool? IsClustered(this IReadOnlyKey key, in StoreObjectIdentifier storeObject)
        {
            if (key is RuntimeKey)
            {
                throw new InvalidOperationException(CoreStrings.RuntimeModelMissingData);
            }

            var annotation = key.FindAnnotation(JetAnnotationNames.Clustered);
            if (annotation != null)
            {
                return (bool?)annotation.Value;
            }

            return GetDefaultIsClustered(key, storeObject);
        }

        private static bool? GetDefaultIsClustered(IReadOnlyKey key, in StoreObjectIdentifier storeObject)
        {
            var sharedTableRootKey = key.FindSharedObjectRootKey(storeObject);
            return sharedTableRootKey?.IsClustered(storeObject);
        }

        /// <summary>
        ///     Sets a value indicating whether the key is clustered.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="clustered">The value to set.</param>
        public static void SetIsClustered(this IMutableKey key, bool? clustered)
            => key.SetOrRemoveAnnotation(JetAnnotationNames.Clustered, clustered);

        /// <summary>
        ///     Sets a value indicating whether the key is clustered.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="clustered">The value to set.</param>
        /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
        /// <returns>The configured value.</returns>
        public static bool? SetIsClustered(this IConventionKey key, bool? clustered, bool fromDataAnnotation = false)
            => (bool?)key.SetOrRemoveAnnotation(
                JetAnnotationNames.Clustered,
                clustered,
                fromDataAnnotation)?.Value;

        /// <summary>
        ///     Gets the <see cref="ConfigurationSource" /> for whether the key is clustered.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The <see cref="ConfigurationSource" /> for whether the key is clustered.</returns>
        public static ConfigurationSource? GetIsClusteredConfigurationSource(this IConventionKey key)
            => key.FindAnnotation(JetAnnotationNames.Clustered)?.GetConfigurationSource();
    }
}