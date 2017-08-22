// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Metadata;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore;

// ReSharper disable once CheckNamespace
namespace EntityFrameworkCore.Jet
{
    /// <summary>
    ///     SQL Server specific extension methods for <see cref="ModelBuilder" />.
    /// </summary>
    public static class JetModelBuilderExtensions
    {
        /// <summary>
        ///     Configures the model to use a sequence-based hi-lo pattern to generate values for key properties
        ///     marked as <see cref="ValueGenerated.OnAdd" />, when targeting SQL Server.
        /// </summary>
        /// <param name="modelBuilder"> The model builder. </param>
        /// <param name="name"> The name of the sequence. </param>
        /// <param name="schema">The schema of the sequence. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static ModelBuilder ForJetUseSequenceHiLo(
            [NotNull] this ModelBuilder modelBuilder,
            [CanBeNull] string name = null,
            [CanBeNull] string schema = null)
        {
            Check.NotNull(modelBuilder, nameof(modelBuilder));
            Check.NullButNotEmpty(name, nameof(name));
            Check.NullButNotEmpty(schema, nameof(schema));

            var model = modelBuilder.Model;

            name = name ?? JetModelAnnotations.DefaultHiLoSequenceName;

            if (model.Jet().FindSequence(name, schema) == null)
            {
                modelBuilder.HasSequence(name, schema).IncrementsBy(10);
            }

            model.Jet().ValueGenerationStrategy = JetValueGenerationStrategy.SequenceHiLo;
            model.Jet().HiLoSequenceName = name;
            model.Jet().HiLoSequenceSchema = schema;

            return modelBuilder;
        }

        /// <summary>
        ///     Configures the model to use the SQL Server IDENTITY feature to generate values for key properties
        ///     marked as <see cref="ValueGenerated.OnAdd" />, when targeting SQL Server. This is the default
        ///     behavior when targeting SQL Server.
        /// </summary>
        /// <param name="modelBuilder"> The model builder. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static ModelBuilder ForJetUseIdentityColumns(
            [NotNull] this ModelBuilder modelBuilder)
        {
            Check.NotNull(modelBuilder, nameof(modelBuilder));

            var property = modelBuilder.Model;

            property.Jet().ValueGenerationStrategy = JetValueGenerationStrategy.IdentityColumn;
            property.Jet().HiLoSequenceName = null;
            property.Jet().HiLoSequenceSchema = null;

            return modelBuilder;
        }
    }
}
