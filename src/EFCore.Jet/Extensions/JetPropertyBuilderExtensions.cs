// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet;
using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EntityFrameworkCore.Jet.Utilities;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    ///     Jet specific extension methods for <see cref="PropertyBuilder" />.
    /// </summary>
    public static class JetPropertyBuilderExtensions
    {
        /// <summary>
        ///     Configures the key property to use the Jet IDENTITY feature to generate values for new entities,
        ///     when targeting Jet. This method sets the property to be <see cref="ValueGenerated.OnAdd" />.
        /// </summary>
        /// <param name="propertyBuilder"> The builder for the property being configured. </param>
        /// <param name="seed"> The value that is used for the very first row loaded into the table. </param>
        /// <param name="increment"> The incremental value that is added to the identity value of the previous row that was loaded. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static PropertyBuilder UseIdentityColumn(
            this PropertyBuilder propertyBuilder,
            int seed = 1,
            int increment = 1)
        {
            Check.NotNull(propertyBuilder, nameof(propertyBuilder));

            var property = propertyBuilder.Metadata;
            property.SetValueGenerationStrategy(JetValueGenerationStrategy.IdentityColumn);
            property.SetIdentitySeed(seed);
            property.SetIdentityIncrement(increment);

            return propertyBuilder;
        }

        /// <summary>
        ///     Configures the key property to use the Jet IDENTITY feature to generate values for new entities,
        ///     when targeting Jet. This method sets the property to be <see cref="ValueGenerated.OnAdd" />.
        /// </summary>
        /// <typeparam name="TProperty"> The type of the property being configured. </typeparam>
        /// <param name="propertyBuilder"> The builder for the property being configured. </param>
        /// <param name="seed"> The value that is used for the very first row loaded into the table. </param>
        /// <param name="increment"> The incremental value that is added to the identity value of the previous row that was loaded. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static PropertyBuilder<TProperty> UseIdentityColumn<TProperty>(
            this PropertyBuilder<TProperty> propertyBuilder,
            int seed = 1,
            int increment = 1)
            => (PropertyBuilder<TProperty>) UseIdentityColumn((PropertyBuilder) propertyBuilder, seed, increment);

        /// <summary>
        ///     Configures the seed for Jet IDENTITY.
        /// </summary>
        /// <param name="propertyBuilder"> The builder for the property being configured. </param>
        /// <param name="seed"> The value that is used for the very first row loaded into the table. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns>
        ///     The same builder instance if the configuration was applied,
        ///     <c>null</c> otherwise.
        /// </returns>
        public static IConventionPropertyBuilder? HasIdentityColumnSeed(
            this IConventionPropertyBuilder propertyBuilder, int? seed, bool fromDataAnnotation = false)
        {
            if (propertyBuilder.CanSetIdentityColumnSeed(seed, fromDataAnnotation))
            {
                propertyBuilder.Metadata.SetIdentitySeed(seed, fromDataAnnotation);
                return propertyBuilder;
            }

            return null;
        }

        /// <summary>
        ///     Returns a value indicating whether the given value can be set as the seed for Jet IDENTITY.
        /// </summary>
        /// <param name="propertyBuilder"> The builder for the property being configured. </param>
        /// <param name="seed"> The value that is used for the very first row loaded into the table. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns> <c>true</c> if the given value can be set as the seed for Jet IDENTITY. </returns>
        public static bool CanSetIdentityColumnSeed(
            this IConventionPropertyBuilder propertyBuilder, int? seed, bool fromDataAnnotation = false)
        {
            Check.NotNull(propertyBuilder, nameof(propertyBuilder));

            return propertyBuilder.CanSetAnnotation(JetAnnotationNames.IdentitySeed, seed, fromDataAnnotation);
        }

        /// <summary>
        ///     Configures the increment for Jet IDENTITY.
        /// </summary>
        /// <param name="propertyBuilder"> The builder for the property being configured. </param>
        /// <param name="increment"> The incremental value that is added to the identity value of the previous row that was loaded. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns>
        ///     The same builder instance if the configuration was applied,
        ///     <c>null</c> otherwise.
        /// </returns>
        public static IConventionPropertyBuilder? HasIdentityColumnIncrement(
            this IConventionPropertyBuilder propertyBuilder, int? increment, bool fromDataAnnotation = false)
        {
            if (propertyBuilder.CanSetIdentityColumnIncrement(increment, fromDataAnnotation))
            {
                propertyBuilder.Metadata.SetIdentityIncrement(increment, fromDataAnnotation);
                return propertyBuilder;
            }

            return null;
        }

        /// <summary>
        ///     Returns a value indicating whether the given value can be set as the increment for Jet IDENTITY.
        /// </summary>
        /// <param name="propertyBuilder"> The builder for the property being configured. </param>
        /// <param name="increment"> The incremental value that is added to the identity value of the previous row that was loaded. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns> <c>true</c> if the given value can be set as the default increment for Jet IDENTITY. </returns>
        public static bool CanSetIdentityColumnIncrement(
            this IConventionPropertyBuilder propertyBuilder, int? increment, bool fromDataAnnotation = false)
        {
            Check.NotNull(propertyBuilder, nameof(propertyBuilder));

            return propertyBuilder.CanSetAnnotation(JetAnnotationNames.IdentityIncrement, increment, fromDataAnnotation);
        }

        /// <summary>
        ///     Configures the value generation strategy for the key property, when targeting Jet.
        /// </summary>
        /// <param name="propertyBuilder"> The builder for the property being configured. </param>
        /// <param name="valueGenerationStrategy"> The value generation strategy. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns>
        ///     The same builder instance if the configuration was applied,
        ///     <c>null</c> otherwise.
        /// </returns>
        public static IConventionPropertyBuilder? HasValueGenerationStrategy(
            this IConventionPropertyBuilder propertyBuilder,
            JetValueGenerationStrategy? valueGenerationStrategy,
            bool fromDataAnnotation = false)
        {
            if (propertyBuilder.CanSetAnnotation(
                JetAnnotationNames.ValueGenerationStrategy, valueGenerationStrategy, fromDataAnnotation))
            {
                propertyBuilder.Metadata.SetValueGenerationStrategy(valueGenerationStrategy, fromDataAnnotation);
                if (valueGenerationStrategy != JetValueGenerationStrategy.IdentityColumn)
                {
                    propertyBuilder.HasIdentityColumnSeed(null, fromDataAnnotation);
                    propertyBuilder.HasIdentityColumnIncrement(null, fromDataAnnotation);
                }

                return propertyBuilder;
            }

            return null;
        }

        /// <summary>
        ///     Returns a value indicating whether the given value can be set as the value generation strategy.
        /// </summary>
        /// <param name="propertyBuilder"> The builder for the property being configured. </param>
        /// <param name="valueGenerationStrategy"> The value generation strategy. </param>
        /// <param name="fromDataAnnotation"> Indicates whether the configuration was specified using a data annotation. </param>
        /// <returns> <c>true</c> if the given value can be set as the default value generation strategy. </returns>
        public static bool CanSetValueGenerationStrategy(
            this IConventionPropertyBuilder propertyBuilder,
            JetValueGenerationStrategy? valueGenerationStrategy,
            bool fromDataAnnotation = false)
        {
            Check.NotNull(propertyBuilder, nameof(propertyBuilder));

            return (valueGenerationStrategy == null
                    || JetPropertyExtensions.IsCompatibleWithValueGeneration(propertyBuilder.Metadata))
                   && propertyBuilder.CanSetAnnotation(
                       JetAnnotationNames.ValueGenerationStrategy, valueGenerationStrategy, fromDataAnnotation);
        }
    }
}