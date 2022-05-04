// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
    public static class JetModelExtensions
    {
        /// <summary>
        ///     Returns the default identity seed.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <returns> The default identity seed. </returns>
        public static int GetIdentitySeed([NotNull] this IModel model)
            => (int?) model[JetAnnotationNames.IdentitySeed] ?? 1;

        /// <summary>
        ///     Sets the default identity seed.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <param name="seed"> The value to set. </param>
        public static void SetIdentitySeed([NotNull] this IMutableModel model, int? seed)
            => model.SetOrRemoveAnnotation(
                JetAnnotationNames.IdentitySeed,
                seed);

        /// <summary>
        ///     Sets the default identity seed.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <param name="seed"> The value to set. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        public static void SetIdentitySeed([NotNull] this IConventionModel model, int? seed, bool fromDataAnnotation = false)
            => model.SetOrRemoveAnnotation(
                JetAnnotationNames.IdentitySeed,
                seed,
                fromDataAnnotation);

        /// <summary>
        ///     Returns the <see cref="ConfigurationSource" /> for the default schema.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <returns> The <see cref="ConfigurationSource" /> for the default schema. </returns>
        public static ConfigurationSource? GetIdentitySeedConfigurationSource([NotNull] this IConventionModel model)
            => model.FindAnnotation(JetAnnotationNames.IdentitySeed)?.GetConfigurationSource();

        /// <summary>
        ///     Returns the default identity increment.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <returns> The default identity increment. </returns>
        public static int GetIdentityIncrement([NotNull] this IModel model)
            => (int?) model[JetAnnotationNames.IdentityIncrement] ?? 1;

        /// <summary>
        ///     Sets the default identity increment.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <param name="increment"> The value to set. </param>
        public static void SetIdentityIncrement([NotNull] this IMutableModel model, int? increment)
            => model.SetOrRemoveAnnotation(
                JetAnnotationNames.IdentityIncrement,
                increment);

        /// <summary>
        ///     Sets the default identity increment.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <param name="increment"> The value to set. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        public static void SetIdentityIncrement(
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
        public static ConfigurationSource? GetIdentityIncrementConfigurationSource([NotNull] this IConventionModel model)
            => model.FindAnnotation(JetAnnotationNames.IdentityIncrement)?.GetConfigurationSource();

        /// <summary>
        ///     Returns the <see cref="JetValueGenerationStrategy" /> to use for properties
        ///     of keys in the model, unless the property has a strategy explicitly set.
        /// </summary>
        /// <param name="model"> The model. </param>
        /// <returns> The default <see cref="JetValueGenerationStrategy" />. </returns>
        public static JetValueGenerationStrategy? GetValueGenerationStrategy([NotNull] this IReadOnlyModel model)
            => (JetValueGenerationStrategy?) model[JetAnnotationNames.ValueGenerationStrategy];

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
        public static ConfigurationSource? GetValueGenerationStrategyConfigurationSource([NotNull] this IConventionModel model)
            => model.FindAnnotation(JetAnnotationNames.ValueGenerationStrategy)?.GetConfigurationSource();
    }
}