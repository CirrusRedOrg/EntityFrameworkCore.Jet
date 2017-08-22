// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using EntityFrameworkCore.Jet.Utilities;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet
{
    /// <summary>
    ///     SQL Server specific extension methods for <see cref="PropertyBuilder" />.
    /// </summary>
    public static class JetPropertyBuilderExtensions
    {
        /// <summary>
        ///     Configures the key property to use a sequence-based hi-lo pattern to generate values for new entities,
        ///     when targeting SQL Server. This method sets the property to be <see cref="ValueGenerated.OnAdd" />.
        /// </summary>
        /// <param name="propertyBuilder"> The builder for the property being configured. </param>
        /// <param name="name"> The name of the sequence. </param>
        /// <param name="schema"> The schema of the sequence. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static PropertyBuilder ForJetUseSequenceHiLo(
            [NotNull] this PropertyBuilder propertyBuilder,
            [CanBeNull] string name = null,
            [CanBeNull] string schema = null)
        {
            Check.NotNull(propertyBuilder, nameof(propertyBuilder));
            Check.NullButNotEmpty(name, nameof(name));
            Check.NullButNotEmpty(schema, nameof(schema));

            var property = propertyBuilder.Metadata;

            name = name ?? JetModelAnnotations.DefaultHiLoSequenceName;

            var model = property.DeclaringEntityType.Model;

            if (model.Jet().FindSequence(name, schema) == null)
            {
                model.Jet().GetOrAddSequence(name, schema).IncrementBy = 10;
            }

            GetJetInternalBuilder(propertyBuilder).ValueGenerationStrategy(JetValueGenerationStrategy.SequenceHiLo);

            property.Jet().HiLoSequenceName = name;
            property.Jet().HiLoSequenceSchema = schema;

            return propertyBuilder;
        }

        /// <summary>
        ///     Configures the key property to use a sequence-based hi-lo pattern to generate values for new entities,
        ///     when targeting SQL Server. This method sets the property to be <see cref="ValueGenerated.OnAdd" />.
        /// </summary>
        /// <typeparam name="TProperty"> The type of the property being configured. </typeparam>
        /// <param name="propertyBuilder"> The builder for the property being configured. </param>
        /// <param name="name"> The name of the sequence. </param>
        /// <param name="schema"> The schema of the sequence. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static PropertyBuilder<TProperty> ForJetUseSequenceHiLo<TProperty>(
            [NotNull] this PropertyBuilder<TProperty> propertyBuilder,
            [CanBeNull] string name = null,
            [CanBeNull] string schema = null)
            => (PropertyBuilder<TProperty>)ForJetUseSequenceHiLo((PropertyBuilder)propertyBuilder, name, schema);

        /// <summary>
        ///     Configures the key property to use the SQL Server IDENTITY feature to generate values for new entities,
        ///     when targeting SQL Server. This method sets the property to be <see cref="ValueGenerated.OnAdd" />.
        /// </summary>
        /// <param name="propertyBuilder"> The builder for the property being configured. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static PropertyBuilder UseJetIdentityColumn(
            [NotNull] this PropertyBuilder propertyBuilder)
        {
            Check.NotNull(propertyBuilder, nameof(propertyBuilder));

            GetJetInternalBuilder(propertyBuilder).ValueGenerationStrategy(JetValueGenerationStrategy.IdentityColumn);

            return propertyBuilder;
        }

        /// <summary>
        ///     Configures the key property to use the SQL Server IDENTITY feature to generate values for new entities,
        ///     when targeting SQL Server. This method sets the property to be <see cref="ValueGenerated.OnAdd" />.
        /// </summary>
        /// <typeparam name="TProperty"> The type of the property being configured. </typeparam>
        /// <param name="propertyBuilder"> The builder for the property being configured. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static PropertyBuilder<TProperty> UseJetIdentityColumn<TProperty>(
            [NotNull] this PropertyBuilder<TProperty> propertyBuilder)
            => (PropertyBuilder<TProperty>)UseJetIdentityColumn((PropertyBuilder)propertyBuilder);

        private static JetPropertyBuilderAnnotations GetJetInternalBuilder(PropertyBuilder propertyBuilder)
            => propertyBuilder.GetInfrastructure<InternalPropertyBuilder>().Jet(ConfigurationSource.Explicit);
    }
}
