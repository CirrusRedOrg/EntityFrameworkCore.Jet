// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Metadata.Internal;
using EntityFrameworkCore.Jet.Utilities;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    ///     Jet specific extension methods for <see cref="ModelBuilder" />.
    /// </summary>
    public static class JetModelBuilderExtensions
    {
        /// <summary>
        ///     Configures the model to use the Jet IDENTITY feature to generate values for key properties
        ///     marked as <see cref="ValueGenerated.OnAdd" />, when targeting Jet. This is the default
        ///     behavior when targeting Jet.
        /// </summary>
        /// <param name="modelBuilder"> The model builder. </param>
        /// <param name="seed"> The value that is used for the very first row loaded into the table. </param>
        /// <param name="increment"> The incremental value that is added to the identity value of the previous row that was loaded. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static ModelBuilder UseJetIdentityColumns(
            this ModelBuilder modelBuilder,
            int seed = 1,
            int increment = 1)
        {
            Check.NotNull(modelBuilder, nameof(modelBuilder));

            var model = modelBuilder.Model;

            model.SetValueGenerationStrategy(JetValueGenerationStrategy.IdentityColumn);
            model.SetJetIdentitySeed(seed);
            model.SetJetIdentityIncrement(increment);

            return modelBuilder;
        }

        /// <summary>
        ///     Configures the default seed for Jet IDENTITY.
        /// </summary>
        /// <param name="modelBuilder"> The model builder. </param>
        /// <param name="seed"> The value that is used for the very first row loaded into the table. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns>
        ///     The same builder instance if the configuration was applied,
        ///     <c>null</c> otherwise.
        /// </returns>
        public static IConventionModelBuilder? HasJetIdentityColumnSeed(
            this IConventionModelBuilder modelBuilder, int? seed, bool fromDataAnnotation = false)
        {
            if (modelBuilder.CanSetJetIdentityColumnSeed(seed, fromDataAnnotation))
            {
                modelBuilder.Metadata.SetJetIdentitySeed(seed, fromDataAnnotation);
                return modelBuilder;
            }

            return null;
        }

        /// <summary>
        ///     Returns a value indicating whether the given value can be set as the default seed for Jet IDENTITY.
        /// </summary>
        /// <param name="modelBuilder"> The model builder. </param>
        /// <param name="seed"> The value that is used for the very first row loaded into the table. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns> <c>true</c> if the given value can be set as the seed for Jet IDENTITY. </returns>
        public static bool CanSetJetIdentityColumnSeed(
            this IConventionModelBuilder modelBuilder, int? seed, bool fromDataAnnotation = false)
        {
            Check.NotNull(modelBuilder, nameof(modelBuilder));

            return modelBuilder.CanSetAnnotation(JetAnnotationNames.IdentitySeed, seed, fromDataAnnotation);
        }

        /// <summary>
        ///     Configures the default increment for Jet IDENTITY.
        /// </summary>
        /// <param name="modelBuilder"> The model builder. </param>
        /// <param name="increment"> The incremental value that is added to the identity value of the previous row that was loaded. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns>
        ///     The same builder instance if the configuration was applied,
        ///     <c>null</c> otherwise.
        /// </returns>
        public static IConventionModelBuilder? HasJetIdentityColumnIncrement(
            this IConventionModelBuilder modelBuilder, int? increment, bool fromDataAnnotation = false)
        {
            if (modelBuilder.CanSetJetIdentityColumnIncrement(increment, fromDataAnnotation))
            {
                modelBuilder.Metadata.SetJetIdentityIncrement(increment, fromDataAnnotation);
                return modelBuilder;
            }

            return null;
        }

        /// <summary>
        ///     Returns a value indicating whether the given value can be set as the default increment for Jet IDENTITY.
        /// </summary>
        /// <param name="modelBuilder"> The model builder. </param>
        /// <param name="increment"> The incremental value that is added to the identity value of the previous row that was loaded. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns> <c>true</c> if the given value can be set as the default increment for Jet IDENTITY. </returns>
        public static bool CanSetJetIdentityColumnIncrement(
            this IConventionModelBuilder modelBuilder, int? increment, bool fromDataAnnotation = false)
        {
            Check.NotNull(modelBuilder, nameof(modelBuilder));

            return modelBuilder.CanSetAnnotation(JetAnnotationNames.IdentityIncrement, increment, fromDataAnnotation);
        }

        /// <summary>
        ///     Configures the default value generation strategy for key properties marked as <see cref="ValueGenerated.OnAdd" />,
        ///     when targeting Jet.
        /// </summary>
        /// <param name="modelBuilder"> The model builder. </param>
        /// <param name="valueGenerationStrategy"> The value generation strategy. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns>
        ///     The same builder instance if the configuration was applied,
        ///     <c>null</c> otherwise.
        /// </returns>
        public static IConventionModelBuilder? HasValueGenerationStrategy(
            this IConventionModelBuilder modelBuilder,
            JetValueGenerationStrategy? valueGenerationStrategy,
            bool fromDataAnnotation = false)
        {
            if (modelBuilder.CanSetValueGenerationStrategy(valueGenerationStrategy, fromDataAnnotation))
            {
                modelBuilder.Metadata.SetValueGenerationStrategy(valueGenerationStrategy, fromDataAnnotation);
                if (valueGenerationStrategy != JetValueGenerationStrategy.IdentityColumn)
                {
                    modelBuilder.HasJetIdentityColumnSeed(null, fromDataAnnotation);
                    modelBuilder.HasJetIdentityColumnIncrement(null, fromDataAnnotation);
                }

                return modelBuilder;
            }

            return null;
        }

        /// <summary>
        ///     Returns a value indicating whether the given value can be set as the default value generation strategy.
        /// </summary>
        /// <param name="modelBuilder"> The model builder. </param>
        /// <param name="valueGenerationStrategy"> The value generation strategy. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns> <c>true</c> if the given value can be set as the default value generation strategy. </returns>
        public static bool CanSetValueGenerationStrategy(
            this IConventionModelBuilder modelBuilder,
            JetValueGenerationStrategy? valueGenerationStrategy,
            bool fromDataAnnotation = false)
        {
            Check.NotNull(modelBuilder, nameof(modelBuilder));

            return modelBuilder.CanSetAnnotation(
                JetAnnotationNames.ValueGenerationStrategy, valueGenerationStrategy, fromDataAnnotation);
        }
    }
}