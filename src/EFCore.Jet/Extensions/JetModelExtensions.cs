// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Metadata.Internal;
using EntityFrameworkCore.Jet.Utilities;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
    public static class JetModelExtensions
    {
        public const string DefaultSequenceNameSuffix = "Sequence";

        /// <summary>
        ///     Returns the default identity seed.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <returns> The default identity seed. </returns>
        public static int GetJetIdentitySeed([NotNull] this IReadOnlyModel model)
            => (int?)model[JetAnnotationNames.IdentitySeed] ?? 1;

        /// <summary>
        ///     Sets the default identity seed.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <param name="seed"> The value to set. </param>
        public static void SetJetIdentitySeed([NotNull] this IMutableModel model, int? seed)
            => model.SetOrRemoveAnnotation(
                JetAnnotationNames.IdentitySeed,
                seed);

        /// <summary>
        ///     Sets the default identity seed.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <param name="seed"> The value to set. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        public static void SetJetIdentitySeed([NotNull] this IConventionModel model, int? seed, bool fromDataAnnotation = false)
            => model.SetOrRemoveAnnotation(
                JetAnnotationNames.IdentitySeed,
                seed,
                fromDataAnnotation);

        /// <summary>
        ///     Returns the <see cref="ConfigurationSource" /> for the default schema.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <returns> The <see cref="ConfigurationSource" /> for the default schema. </returns>
        public static ConfigurationSource? GetJetIdentitySeedConfigurationSource([NotNull] this IConventionModel model)
            => model.FindAnnotation(JetAnnotationNames.IdentitySeed)?.GetConfigurationSource();

        /// <summary>
        ///     Returns the default identity increment.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <returns> The default identity increment. </returns>
        public static int GetJetIdentityIncrement([NotNull] this IReadOnlyModel model)
            => (int?)model[JetAnnotationNames.IdentityIncrement] ?? 1;

        /// <summary>
        ///     Sets the default identity increment.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <param name="increment"> The value to set. </param>
        public static void SetJetIdentityIncrement([NotNull] this IMutableModel model, int? increment)
            => model.SetOrRemoveAnnotation(
                JetAnnotationNames.IdentityIncrement,
                increment);

        /// <summary>
        ///     Sets the default identity increment.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <param name="increment"> The value to set. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        public static void SetJetIdentityIncrement(
            [NotNull] this IConventionModel model, int? increment, bool fromDataAnnotation = false)
            => model.SetOrRemoveAnnotation(
                JetAnnotationNames.IdentityIncrement,
                increment,
                fromDataAnnotation);

        /// <summary>
        ///     Returns the <see cref="ConfigurationSource" /> for the default identity increment.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <returns> The <see cref="ConfigurationSource" /> for the default identity increment. </returns>
        public static ConfigurationSource? GetJetIdentityIncrementConfigurationSource([NotNull] this IConventionModel model)
            => model.FindAnnotation(JetAnnotationNames.IdentityIncrement)?.GetConfigurationSource();

        /// <summary>
        ///     Returns the <see cref="JetValueGenerationStrategy" /> to use for properties
        ///     of keys in the model, unless the property has a strategy explicitly set.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <returns> The default <see cref="JetValueGenerationStrategy" />. </returns>
        public static JetValueGenerationStrategy? GetValueGenerationStrategy([NotNull] this IReadOnlyModel model)
            => (JetValueGenerationStrategy?)model[JetAnnotationNames.ValueGenerationStrategy];

        /// <summary>
        ///     Sets the <see cref="JetValueGenerationStrategy" /> to use for properties
        ///     of keys in the model that don't have a strategy explicitly set.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <param name="value"> The value to set. </param>
        public static void SetValueGenerationStrategy([NotNull] this IMutableModel model, JetValueGenerationStrategy? value)
            => model.SetOrRemoveAnnotation(JetAnnotationNames.ValueGenerationStrategy, value);

        /// <summary>
        ///     Sets the <see cref="JetValueGenerationStrategy" /> to use for properties
        ///     of keys in the model that don't have a strategy explicitly set.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <param name="value"> The value to set. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        public static void SetValueGenerationStrategy(
            [NotNull] this IConventionModel model, JetValueGenerationStrategy? value, bool fromDataAnnotation = false)
            => model.SetOrRemoveAnnotation(JetAnnotationNames.ValueGenerationStrategy, value, fromDataAnnotation);

        /// <summary>
        ///     Returns the <see cref="ConfigurationSource" /> for the default <see cref="JetValueGenerationStrategy" />.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <returns> The <see cref="ConfigurationSource" /> for the default <see cref="JetValueGenerationStrategy" />. </returns>
        public static ConfigurationSource? GetJetValueGenerationStrategyConfigurationSource([NotNull] this IConventionModel model)
            => model.FindAnnotation(JetAnnotationNames.ValueGenerationStrategy)?.GetConfigurationSource();

        /// <summary>
        ///     Returns the suffix to append to the name of automatically created sequences.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <returns>The name to use for the default key value generation sequence.</returns>
        public static string GetJetSequenceNameSuffix(this IReadOnlyModel model)
            => (string?)model[JetAnnotationNames.SequenceNameSuffix]
                ?? DefaultSequenceNameSuffix;

        /// <summary>
        ///     Sets the suffix to append to the name of automatically created sequences.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <param name="name">The value to set.</param>
        public static void SetJetSequenceNameSuffix(this IMutableModel model, string? name)
        {
            Check.NullButNotEmpty(name, nameof(name));

            model.SetOrRemoveAnnotation(JetAnnotationNames.SequenceNameSuffix, name);
        }

        /// <summary>
        ///     Sets the suffix to append to the name of automatically created sequences.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <param name="name">The value to set.</param>
        /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
        /// <returns>The configured value.</returns>
        public static string? SetJetSequenceNameSuffix(
            this IConventionModel model,
            string? name,
            bool fromDataAnnotation = false)
            => (string?)model.SetOrRemoveAnnotation(
                JetAnnotationNames.SequenceNameSuffix,
                Check.NullButNotEmpty(name, nameof(name)),
                fromDataAnnotation)?.Value;

        /// <summary>
        ///     Returns the <see cref="ConfigurationSource" /> for the default value generation sequence name suffix.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <returns>The <see cref="ConfigurationSource" /> for the default key value generation sequence name.</returns>
        public static ConfigurationSource? GetJetSequenceNameSuffixConfigurationSource(this IConventionModel model)
            => model.FindAnnotation(JetAnnotationNames.SequenceNameSuffix)?.GetConfigurationSource();

        /// <summary>
        ///     Returns the schema to use for the default value generation sequence.
        ///     <see cref="JetPropertyBuilderExtensions.UseSequence" />
        /// </summary>
        /// <param name="model">The model.</param>
        /// <returns>The schema to use for the default key value generation sequence.</returns>
        public static string? GetJetSequenceSchema(this IReadOnlyModel model)
            => (string?)model[JetAnnotationNames.SequenceSchema];

        /// <summary>
        ///     Sets the schema to use for the default key value generation sequence.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <param name="value">The value to set.</param>
        public static void SetJetSequenceSchema(this IMutableModel model, string? value)
        {
            Check.NullButNotEmpty(value, nameof(value));

            model.SetOrRemoveAnnotation(JetAnnotationNames.SequenceSchema, value);
        }

        /// <summary>
        ///     Sets the schema to use for the default key value generation sequence.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="fromDataAnnotation">Indicates whether the configuration was specified using a data annotation.</param>
        /// <returns>The configured value.</returns>
        public static string? SetJetSequenceSchema(
            this IConventionModel model,
            string? value,
            bool fromDataAnnotation = false)
            => (string?)model.SetOrRemoveAnnotation(
                JetAnnotationNames.SequenceSchema,
                Check.NullButNotEmpty(value, nameof(value)),
                fromDataAnnotation)?.Value;

        /// <summary>
        ///     Returns the <see cref="ConfigurationSource" /> for the default key value generation sequence schema.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <returns>The <see cref="ConfigurationSource" /> for the default key value generation sequence schema.</returns>
        public static ConfigurationSource? GetJetSequenceSchemaConfigurationSource(this IConventionModel model)
            => model.FindAnnotation(JetAnnotationNames.SequenceSchema)?.GetConfigurationSource();
    }
}