// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.Jet.Internal;
using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Linq;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore.Storage;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    ///     Extension methods for <see cref="IProperty" /> for Jet-specific metadata.
    /// </summary>
    public static class JetPropertyExtensions
    {
        /// <summary>
        ///     Returns the identity seed.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <returns> The identity seed. </returns>
        public static int? GetJetIdentitySeed([NotNull] this IReadOnlyProperty property)
            => (int?)property[JetAnnotationNames.IdentitySeed];

        /// <summary>
        ///     Returns the identity seed.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <param name="storeObject"> The identifier of the store object. </param>
        /// <returns> The identity seed. </returns>
        public static int? GetJetIdentitySeed([NotNull] this IReadOnlyProperty property, in StoreObjectIdentifier storeObject)
        {
            var annotation = property.FindAnnotation(JetAnnotationNames.IdentitySeed);
            if (annotation != null)
            {
                return (int?)annotation.Value;
            }

            var sharedTableRootProperty = property.FindSharedStoreObjectRootProperty(storeObject);
            return sharedTableRootProperty != null
                ? sharedTableRootProperty.GetJetIdentitySeed(storeObject)
                : null;
        }

        /// <summary>
        ///     Sets the identity seed.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <param name="seed"> The value to set. </param>
        public static void SetJetIdentitySeed([NotNull] this IMutableProperty property, int? seed)
            => property.SetOrRemoveAnnotation(
                JetAnnotationNames.IdentitySeed,
                seed);

        /// <summary>
        ///     Sets the identity seed.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <param name="seed"> The value to set. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns> The configured value. </returns>
        public static int? SetJetIdentitySeed(
            [NotNull] this IConventionProperty property,
            int? seed,
            bool fromDataAnnotation = false)
        {
            property.SetOrRemoveAnnotation(
                JetAnnotationNames.IdentitySeed,
                seed,
                fromDataAnnotation);

            return seed;
        }

        /// <summary>
        ///     Returns the <see cref="ConfigurationSource" /> for the identity seed.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <returns> The <see cref="ConfigurationSource" /> for the identity seed. </returns>
        public static ConfigurationSource? GetJetIdentitySeedConfigurationSource([NotNull] this IConventionProperty property)
            => property.FindAnnotation(JetAnnotationNames.IdentitySeed)?.GetConfigurationSource();

        /// <summary>
        ///     Returns the identity increment.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <returns> The identity increment. </returns>
        public static int? GetJetIdentityIncrement([NotNull] this IReadOnlyProperty property)
            => (int?)property[JetAnnotationNames.IdentityIncrement];

        /// <summary>
        ///     Returns the identity increment.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <param name="storeObject"> The identifier of the store object. </param>
        /// <returns> The identity increment. </returns>
        public static int? GetJetIdentityIncrement([NotNull] this IReadOnlyProperty property, in StoreObjectIdentifier storeObject)
        {
            var annotation = property.FindAnnotation(JetAnnotationNames.IdentityIncrement);
            if (annotation != null)
            {
                return (int?)annotation.Value;
            }

            var sharedTableRootProperty = property.FindSharedStoreObjectRootProperty(storeObject);
            return sharedTableRootProperty != null
                ? sharedTableRootProperty.GetJetIdentityIncrement(storeObject)
                : null;
        }

        /// <summary>
        ///     Sets the identity increment.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <param name="increment"> The value to set. </param>
        public static void SetJetIdentityIncrement([NotNull] this IMutableProperty property, int? increment)
            => property.SetOrRemoveAnnotation(
                JetAnnotationNames.IdentityIncrement,
                increment);

        /// <summary>
        ///     Sets the identity increment.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <param name="increment"> The value to set. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns> The configured value. </returns>
        public static int? SetJetIdentityIncrement(
            [NotNull] this IConventionProperty property,
            int? increment,
            bool fromDataAnnotation = false)
        {
            property.SetOrRemoveAnnotation(
                JetAnnotationNames.IdentityIncrement,
                increment,
                fromDataAnnotation);

            return increment;
        }

        /// <summary>
        ///     Returns the <see cref="ConfigurationSource" /> for the identity increment.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <returns> The <see cref="ConfigurationSource" /> for the identity increment. </returns>
        public static ConfigurationSource? GetJetIdentityIncrementConfigurationSource([NotNull] this IConventionProperty property)
            => property.FindAnnotation(JetAnnotationNames.IdentityIncrement)?.GetConfigurationSource();

        /// <summary>
        ///     <para>
        ///         Returns the <see cref="JetValueGenerationStrategy" /> to use for the property.
        ///     </para>
        ///     <para>
        ///         If no strategy is set for the property, then the strategy to use will be taken from the <see cref="IModel" />.
        ///     </para>
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <returns> The strategy, or <see cref="JetValueGenerationStrategy.None" /> if none was set. </returns>
        public static JetValueGenerationStrategy GetValueGenerationStrategy([NotNull] this IReadOnlyProperty property)
        {
            var annotation = property.FindAnnotation(JetAnnotationNames.ValueGenerationStrategy);
            if (annotation != null)
            {
                return (JetValueGenerationStrategy?)annotation.Value ?? JetValueGenerationStrategy.None;
            }

            if (property.ValueGenerated != ValueGenerated.OnAdd
                || property.IsForeignKey()
                || property.TryGetDefaultValue(out _)
                || property.GetDefaultValueSql() != null
                || property.GetComputedColumnSql() != null)
            {
                return JetValueGenerationStrategy.None;
            }

            return GetDefaultValueGenerationStrategy(property);
        }
        /// <summary>
        ///     <para>
        ///         Returns the <see cref="JetValueGenerationStrategy" /> to use for the property.
        ///     </para>
        ///     <para>
        ///         If no strategy is set for the property, then the strategy to use will be taken from the <see cref="IModel" />.
        ///     </para>
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <param name="storeObject"> The identifier of the store object. </param>
        /// <returns> The strategy, or <see cref="JetValueGenerationStrategy.None" /> if none was set. </returns>
        public static JetValueGenerationStrategy GetValueGenerationStrategy(
            this IReadOnlyProperty property,
            in StoreObjectIdentifier storeObject)
            => GetValueGenerationStrategy(property, storeObject, null);

        internal static JetValueGenerationStrategy GetValueGenerationStrategy(
            this IReadOnlyProperty property,
            in StoreObjectIdentifier storeObject,
            ITypeMappingSource? typeMappingSource)
        {
            var @override = property.FindOverrides(storeObject)?.FindAnnotation(JetAnnotationNames.ValueGenerationStrategy);
            if (@override != null)
            {
                return (JetValueGenerationStrategy?)@override.Value ?? JetValueGenerationStrategy.None;
            }

            var annotation = property.FindAnnotation(JetAnnotationNames.ValueGenerationStrategy);
            if (annotation?.Value != null
                && StoreObjectIdentifier.Create(property.DeclaringType, storeObject.StoreObjectType) == storeObject)
            {
                return (JetValueGenerationStrategy)annotation.Value;
            }

            var table = storeObject;
            var sharedTableRootProperty = property.FindSharedStoreObjectRootProperty(storeObject);
            if (sharedTableRootProperty != null)
            {
                return sharedTableRootProperty.GetValueGenerationStrategy(storeObject, typeMappingSource)
                       == JetValueGenerationStrategy.IdentityColumn
                       && table.StoreObjectType == StoreObjectType.Table
                       && !property.GetContainingForeignKeys().Any(
                           fk =>
                               !fk.IsBaseLinking()
                               || (StoreObjectIdentifier.Create(fk.PrincipalEntityType, StoreObjectType.Table)
                                       is StoreObjectIdentifier principal
                                   && fk.GetConstraintName(table, principal) != null))
                    ? JetValueGenerationStrategy.IdentityColumn
                    : JetValueGenerationStrategy.None;
            }

            if (property.ValueGenerated != ValueGenerated.OnAdd
                || table.StoreObjectType != StoreObjectType.Table
                || property.TryGetDefaultValue(storeObject, out _)
                || property.GetDefaultValueSql(storeObject) != null
                || property.GetComputedColumnSql(storeObject) != null
                || property.GetContainingForeignKeys()
                    .Any(
                        fk =>
                            !fk.IsBaseLinking()
                            || (StoreObjectIdentifier.Create(fk.PrincipalEntityType, StoreObjectType.Table)
                                    is StoreObjectIdentifier principal
                                && fk.GetConstraintName(table, principal) != null)))
            {
                return JetValueGenerationStrategy.None;
            }

            var defaultStrategy = GetDefaultValueGenerationStrategy(property, storeObject, typeMappingSource);
            if (defaultStrategy != JetValueGenerationStrategy.None)
            {
                if (annotation != null)
                {
                    return (JetValueGenerationStrategy?)annotation.Value ?? JetValueGenerationStrategy.None;
                }
            }

            return defaultStrategy;
        }

        private static JetValueGenerationStrategy GetDefaultValueGenerationStrategy(IReadOnlyProperty property)
        {
            var modelStrategy = property.DeclaringType.Model.GetValueGenerationStrategy();

            return modelStrategy == JetValueGenerationStrategy.IdentityColumn
                   && IsCompatibleWithValueGeneration(property)
                    ? JetValueGenerationStrategy.IdentityColumn
                    : JetValueGenerationStrategy.None;
        }

        private static JetValueGenerationStrategy GetDefaultValueGenerationStrategy(
            IReadOnlyProperty property,
            in StoreObjectIdentifier storeObject,
            ITypeMappingSource? typeMappingSource)
        {
            var modelStrategy = property.DeclaringType.Model.GetValueGenerationStrategy();

            return modelStrategy == JetValueGenerationStrategy.IdentityColumn
                   && IsCompatibleWithValueGeneration(property, storeObject, typeMappingSource)
                ? property.DeclaringType.GetMappingStrategy() == RelationalAnnotationNames.TpcMappingStrategy
                    ? JetValueGenerationStrategy.None
                    : JetValueGenerationStrategy.IdentityColumn
                : JetValueGenerationStrategy.None;
        }

        /// <summary>
        ///     Sets the <see cref="JetValueGenerationStrategy" /> to use for the property.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <param name="value"> The strategy to use. </param>
        public static void SetValueGenerationStrategy(
            [NotNull] this IMutableProperty property, JetValueGenerationStrategy? value)
        {
            CheckValueGenerationStrategy(property, value);

            property.SetOrRemoveAnnotation(JetAnnotationNames.ValueGenerationStrategy, value);
        }

        /// <summary>
        ///     Sets the <see cref="JetValueGenerationStrategy" /> to use for the property.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <param name="value"> The strategy to use. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns> The configured value. </returns>
        public static JetValueGenerationStrategy? SetValueGenerationStrategy(
            [NotNull] this IConventionProperty property,
            JetValueGenerationStrategy? value,
            bool fromDataAnnotation = false)
        {
            CheckValueGenerationStrategy(property, value);

            property.SetOrRemoveAnnotation(JetAnnotationNames.ValueGenerationStrategy, value, fromDataAnnotation);

            return value;
        }

        private static void CheckValueGenerationStrategy(IReadOnlyProperty property, JetValueGenerationStrategy? value)
        {
            if (value != null)
            {
                var propertyType = property.ClrType;

                if (value == JetValueGenerationStrategy.IdentityColumn
                    && !IsCompatibleWithValueGeneration(property))
                {
                    throw new ArgumentException(
                        JetStrings.IdentityBadType(
                            property.Name, property.DeclaringType.DisplayName(), propertyType.ShortDisplayName()));
                }
            }
        }

        /// <summary>
        ///     Returns the <see cref="ConfigurationSource" /> for the <see cref="JetValueGenerationStrategy" />.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <returns> The <see cref="ConfigurationSource" /> for the <see cref="JetValueGenerationStrategy" />. </returns>
        public static ConfigurationSource? GetJetValueGenerationStrategyConfigurationSource(
            [NotNull] this IConventionProperty property)
            => property.FindAnnotation(JetAnnotationNames.ValueGenerationStrategy)?.GetConfigurationSource();

        /// <summary>
        ///     Returns a value indicating whether the property is compatible with any <see cref="JetValueGenerationStrategy" />.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <returns> <c>true</c> if compatible. </returns>
        public static bool IsCompatibleWithValueGeneration([NotNull] IReadOnlyProperty property)
        {
            var valueConverter = property.GetValueConverter()
                                 ?? property.FindTypeMapping()?.Converter;

            var type = (valueConverter?.ProviderClrType ?? property.ClrType).UnwrapNullableType();
            return type.IsInteger()
                   || type.IsEnum
                   || type == typeof(decimal);
        }

        private static bool IsCompatibleWithValueGeneration(
            IReadOnlyProperty property,
            in StoreObjectIdentifier storeObject,
            ITypeMappingSource? typeMappingSource)
        {
            if (storeObject.StoreObjectType != StoreObjectType.Table)
            {
                return false;
            }

            var valueConverter = property.GetValueConverter()
                                 ?? (property.FindRelationalTypeMapping(storeObject)
                                     ?? typeMappingSource?.FindMapping((IProperty)property))?.Converter;

            var type = (valueConverter?.ProviderClrType ?? property.ClrType).UnwrapNullableType();

            return (type.IsInteger()
                    || type == typeof(decimal));
        }
    }
}