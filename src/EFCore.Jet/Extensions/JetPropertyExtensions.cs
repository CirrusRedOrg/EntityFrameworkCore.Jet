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
            [NotNull] this IReadOnlyProperty property,
            in StoreObjectIdentifier storeObject)
        {
            var annotation = property.FindAnnotation(JetAnnotationNames.ValueGenerationStrategy);
            if (annotation != null)
            {
                return (JetValueGenerationStrategy?)annotation.Value ?? JetValueGenerationStrategy.None;
            }

            var sharedTableRootProperty = property.FindSharedStoreObjectRootProperty(storeObject);
            if (sharedTableRootProperty != null)
            {
                return sharedTableRootProperty.GetValueGenerationStrategy(storeObject)
                    == JetValueGenerationStrategy.IdentityColumn
                        ? JetValueGenerationStrategy.IdentityColumn
                        : JetValueGenerationStrategy.None;
            }

            if (property.ValueGenerated != ValueGenerated.OnAdd
                || property.GetContainingForeignKeys().Any(fk => !fk.IsBaseLinking())
                || property.TryGetDefaultValue(storeObject, out _)
                || property.GetDefaultValueSql(storeObject) != null
                || property.GetComputedColumnSql(storeObject) != null)
            {
                return JetValueGenerationStrategy.None;
            }

            return GetDefaultValueGenerationStrategy(property);
        }

        private static JetValueGenerationStrategy GetDefaultValueGenerationStrategy(IReadOnlyProperty property)
        {
            var modelStrategy = property.DeclaringType.Model.GetValueGenerationStrategy();

            return modelStrategy == JetValueGenerationStrategy.IdentityColumn
                   && IsCompatibleWithValueGeneration(property)
                    ? JetValueGenerationStrategy.IdentityColumn
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
            var type = property.ClrType;

            return (type.IsInteger()
                    || type == typeof(decimal))
                   && (property.GetValueConverter()
                       ?? property.FindTypeMapping()?.Converter)
                   == null;
        }

        /// <summary>
        ///     Returns the name to use for the key value generation sequence.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <returns>The name to use for the key value generation sequence.</returns>
        public static string? GetJetSequenceName(this IReadOnlyProperty property)
            => (string?)property[JetAnnotationNames.SequenceName];

        /// <summary>
        ///     Returns the name to use for the key value generation sequence.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="storeObject">The identifier of the store object.</param>
        /// <returns>The name to use for the key value generation sequence.</returns>
        public static string? GetJetSequenceName(this IReadOnlyProperty property, in StoreObjectIdentifier storeObject)
        {
            var annotation = property.FindAnnotation(JetAnnotationNames.SequenceName);
            if (annotation != null)
            {
                return (string?)annotation.Value;
            }

            return property.FindSharedStoreObjectRootProperty(storeObject)?.GetJetSequenceName(storeObject);
        }

        /// <summary>
        ///     Sets the name to use for the key value generation sequence.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="name">The sequence name to use.</param>
        public static void SetJetSequenceName(this IMutableProperty property, string? name)
            => property.SetOrRemoveAnnotation(
                JetAnnotationNames.SequenceName,
                Check.NullButNotEmpty(name, nameof(name)));

        /// <summary>
        ///     Sets the name to use for the key value generation sequence.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="name">The sequence name to use.</param>
        /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
        /// <returns>The configured value.</returns>
        public static string? SetJetSequenceName(
            this IConventionProperty property,
            string? name,
            bool fromDataAnnotation = false)
            => (string?)property.SetOrRemoveAnnotation(
                JetAnnotationNames.SequenceName,
                Check.NullButNotEmpty(name, nameof(name)),
                fromDataAnnotation)?.Value;

        /// <summary>
        ///     Returns the <see cref="ConfigurationSource" /> for the key value generation sequence name.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <returns>The <see cref="ConfigurationSource" /> for the key value generation sequence name.</returns>
        public static ConfigurationSource? GetJetSequenceNameConfigurationSource(this IConventionProperty property)
            => property.FindAnnotation(JetAnnotationNames.SequenceName)?.GetConfigurationSource();

        /// <summary>
        ///     Returns the schema to use for the key value generation sequence.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <returns>The schema to use for the key value generation sequence.</returns>
        public static string? GetJetSequenceSchema(this IReadOnlyProperty property)
            => (string?)property[JetAnnotationNames.SequenceSchema];

        /// <summary>
        ///     Returns the schema to use for the key value generation sequence.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="storeObject">The identifier of the store object.</param>
        /// <returns>The schema to use for the key value generation sequence.</returns>
        public static string? GetJetSequenceSchema(this IReadOnlyProperty property, in StoreObjectIdentifier storeObject)
        {
            var annotation = property.FindAnnotation(JetAnnotationNames.SequenceSchema);
            if (annotation != null)
            {
                return (string?)annotation.Value;
            }

            return property.FindSharedStoreObjectRootProperty(storeObject)?.GetJetSequenceSchema(storeObject);
        }

        /// <summary>
        ///     Sets the schema to use for the key value generation sequence.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="schema">The schema to use.</param>
        public static void SetJetSequenceSchema(this IMutableProperty property, string? schema)
            => property.SetOrRemoveAnnotation(
                JetAnnotationNames.SequenceSchema,
                Check.NullButNotEmpty(schema, nameof(schema)));

        /// <summary>
        ///     Sets the schema to use for the key value generation sequence.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="schema">The schema to use.</param>
        /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
        /// <returns>The configured value.</returns>
        public static string? SetJetSequenceSchema(
            this IConventionProperty property,
            string? schema,
            bool fromDataAnnotation = false)
            => (string?)property.SetOrRemoveAnnotation(
                JetAnnotationNames.SequenceSchema,
                Check.NullButNotEmpty(schema, nameof(schema)),
                fromDataAnnotation)?.Value;

        /// <summary>
        ///     Returns the <see cref="ConfigurationSource" /> for the key value generation sequence schema.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <returns>The <see cref="ConfigurationSource" /> for the key value generation sequence schema.</returns>
        public static ConfigurationSource? GetGetSequenceSchemaConfigurationSource(this IConventionProperty property)
            => property.FindAnnotation(JetAnnotationNames.SequenceSchema)?.GetConfigurationSource();

        /// <summary>
        ///     Finds the <see cref="ISequence" /> in the model to use for the key value generation pattern.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <returns>The sequence to use, or <see langword="null" /> if no sequence exists in the model.</returns>
        public static IReadOnlySequence? FindJetSequence(this IReadOnlyProperty property)
        {
            var model = property.DeclaringType.Model;

            var sequenceName = property.GetJetSequenceName()
                ?? model.GetJetSequenceNameSuffix();

            var sequenceSchema = property.GetJetSequenceSchema()
                ?? model.GetJetSequenceSchema();

            return model.FindSequence(sequenceName, sequenceSchema);
        }

        /// <summary>
        ///     Finds the <see cref="ISequence" /> in the model to use for the key value generation pattern.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="storeObject">The identifier of the store object.</param>
        /// <returns>The sequence to use, or <see langword="null" /> if no sequence exists in the model.</returns>
        public static IReadOnlySequence? FindJetSequence(this IReadOnlyProperty property, in StoreObjectIdentifier storeObject)
        {
            var model = property.DeclaringType.Model;

            var sequenceName = property.GetJetSequenceName(storeObject)
                ?? model.GetJetSequenceNameSuffix();

            var sequenceSchema = property.GetJetSequenceSchema(storeObject)
                ?? model.GetJetSequenceSchema();

            return model.FindSequence(sequenceName, sequenceSchema);
        }

        /// <summary>
        ///     Finds the <see cref="ISequence" /> in the model to use for the key value generation pattern.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <returns>The sequence to use, or <see langword="null" /> if no sequence exists in the model.</returns>
        public static ISequence? FindJetSequence(this IProperty property)
            => (ISequence?)((IReadOnlyProperty)property).FindJetSequence();

        /// <summary>
        ///     Finds the <see cref="ISequence" /> in the model to use for the key value generation pattern.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="storeObject">The identifier of the store object.</param>
        /// <returns>The sequence to use, or <see langword="null" /> if no sequence exists in the model.</returns>
        public static ISequence? FindJetSequence(this IProperty property, in StoreObjectIdentifier storeObject)
            => (ISequence?)((IReadOnlyProperty)property).FindJetSequence(storeObject);
    }
}