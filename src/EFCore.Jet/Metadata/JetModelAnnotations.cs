// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using EntityFrameworkCore.Jet.Utilities;

namespace EntityFrameworkCore.Jet.Metadata
{
    public class JetModelAnnotations : RelationalModelAnnotations, IJetModelAnnotations
    {
        public const string DefaultHiLoSequenceName = "EntityFrameworkHiLoSequence";

        public JetModelAnnotations([NotNull] IModel model)
            : base(model)
        {
        }

        protected JetModelAnnotations([NotNull] RelationalAnnotations annotations)
            : base(annotations)
        {
        }

        public virtual string HiLoSequenceName
        {
            get => (string)Annotations.Metadata[JetAnnotationNames.HiLoSequenceName];
            [param: CanBeNull] set => SetHiLoSequenceName(value);
        }

        protected virtual bool SetHiLoSequenceName([CanBeNull] string value)
            => Annotations.SetAnnotation(
                JetAnnotationNames.HiLoSequenceName,
                Check.NullButNotEmpty(value, nameof(value)));

        public virtual string HiLoSequenceSchema
        {
            get => (string)Annotations.Metadata[JetAnnotationNames.HiLoSequenceSchema];
            [param: CanBeNull] set => SetHiLoSequenceSchema(value);
        }

        protected virtual bool SetHiLoSequenceSchema([CanBeNull] string value)
            => Annotations.SetAnnotation(
                JetAnnotationNames.HiLoSequenceSchema,
                Check.NullButNotEmpty(value, nameof(value)));

        public virtual JetValueGenerationStrategy? ValueGenerationStrategy
        {
            get => (JetValueGenerationStrategy?)Annotations.Metadata[JetAnnotationNames.ValueGenerationStrategy];

            set => SetValueGenerationStrategy(value);
        }

        protected virtual bool SetValueGenerationStrategy(JetValueGenerationStrategy? value)
            => Annotations.SetAnnotation(JetAnnotationNames.ValueGenerationStrategy, value);
    }
}
