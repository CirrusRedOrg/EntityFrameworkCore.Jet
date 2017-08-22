// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.Jet.Metadata.Internal;
using EntityFrameworkCore.Jet.Properties;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using EntityFrameworkCore.Jet.Utilities;

namespace EntityFrameworkCore.Jet.Metadata
{
    public class JetPropertyAnnotations : RelationalPropertyAnnotations, IJetPropertyAnnotations
    {
        public JetPropertyAnnotations([NotNull] IProperty property)
            : base(property)
        {
        }

        protected JetPropertyAnnotations([NotNull] RelationalAnnotations annotations)
            : base(annotations)
        {
        }

        public virtual string HiLoSequenceName
        {
            get { return (string)Annotations.Metadata[JetAnnotationNames.HiLoSequenceName]; }
            [param: CanBeNull] set { SetHiLoSequenceName(value); }
        }

        protected virtual bool SetHiLoSequenceName([CanBeNull] string value)
            => Annotations.SetAnnotation(
                JetAnnotationNames.HiLoSequenceName,
                Check.NullButNotEmpty(value, nameof(value)));

        public virtual string HiLoSequenceSchema
        {
            get { return (string)Annotations.Metadata[JetAnnotationNames.HiLoSequenceSchema]; }
            [param: CanBeNull] set { SetHiLoSequenceSchema(value); }
        }

        protected virtual bool SetHiLoSequenceSchema([CanBeNull] string value)
            => Annotations.SetAnnotation(
                JetAnnotationNames.HiLoSequenceSchema,
                Check.NullButNotEmpty(value, nameof(value)));

        public virtual ISequence FindHiLoSequence()
        {
            var modelExtensions = Property.DeclaringEntityType.Model.Jet();

            if (ValueGenerationStrategy != JetValueGenerationStrategy.SequenceHiLo)
            {
                return null;
            }

            var sequenceName = HiLoSequenceName
                               ?? modelExtensions.HiLoSequenceName
                               ?? JetModelAnnotations.DefaultHiLoSequenceName;

            var sequenceSchema = HiLoSequenceSchema
                                 ?? modelExtensions.HiLoSequenceSchema;

            return modelExtensions.FindSequence(sequenceName, sequenceSchema);
        }

        public virtual JetValueGenerationStrategy? ValueGenerationStrategy
        {
            get => GetJetValueGenerationStrategy(fallbackToModel: true);
            set => SetValueGenerationStrategy(value);
        }

        public virtual JetValueGenerationStrategy? GetJetValueGenerationStrategy(bool fallbackToModel)
        {
            var value = (JetValueGenerationStrategy?)Annotations.Metadata[JetAnnotationNames.ValueGenerationStrategy];

            if (value != null)
            {
                return value;
            }

            var relationalProperty = Property.Relational();
            if (!fallbackToModel
                || Property.ValueGenerated != ValueGenerated.OnAdd
                || relationalProperty.DefaultValue != null
                || relationalProperty.DefaultValueSql != null
                || relationalProperty.ComputedColumnSql != null)
            {
                return null;
            }

            var modelStrategy = Property.DeclaringEntityType.Model.Jet().ValueGenerationStrategy;

            if (modelStrategy == JetValueGenerationStrategy.SequenceHiLo
                && IsCompatibleSequenceHiLo(Property.ClrType))
            {
                return JetValueGenerationStrategy.SequenceHiLo;
            }

            if (modelStrategy == JetValueGenerationStrategy.IdentityColumn
                && IsCompatibleIdentityColumn(Property.ClrType))
            {
                return JetValueGenerationStrategy.IdentityColumn;
            }

            return null;
        }

        protected virtual bool SetValueGenerationStrategy(JetValueGenerationStrategy? value)
        {
            if (value != null)
            {
                var propertyType = Property.ClrType;

                if (value == JetValueGenerationStrategy.IdentityColumn
                    && !IsCompatibleIdentityColumn(propertyType))
                {
                    if (ShouldThrowOnInvalidConfiguration)
                    {
                        throw new ArgumentException(
                            JetStrings.IdentityBadType(
                                Property.Name, Property.DeclaringEntityType.DisplayName(), propertyType.ShortDisplayName()));
                    }

                    return false;
                }

                if (value == JetValueGenerationStrategy.SequenceHiLo
                    && !IsCompatibleSequenceHiLo(propertyType))
                {
                    if (ShouldThrowOnInvalidConfiguration)
                    {
                        throw new ArgumentException(
                            JetStrings.SequenceBadType(
                                Property.Name, Property.DeclaringEntityType.DisplayName(), propertyType.ShortDisplayName()));
                    }

                    return false;
                }
            }

            if (!CanSetValueGenerationStrategy(value))
            {
                return false;
            }

            if (!ShouldThrowOnConflict
                && ValueGenerationStrategy != value
                && value != null)
            {
                ClearAllServerGeneratedValues();
            }

            return Annotations.SetAnnotation(JetAnnotationNames.ValueGenerationStrategy, value);
        }

        protected virtual bool CanSetValueGenerationStrategy(JetValueGenerationStrategy? value)
        {
            if (GetJetValueGenerationStrategy(fallbackToModel: false) == value)
            {
                return true;
            }

            if (!Annotations.CanSetAnnotation(JetAnnotationNames.ValueGenerationStrategy, value))
            {
                return false;
            }

            if (ShouldThrowOnConflict)
            {
                if (GetDefaultValue(false) != null)
                {
                    throw new InvalidOperationException(
                        RelationalStrings.ConflictingColumnServerGeneration(nameof(ValueGenerationStrategy), Property.Name, nameof(DefaultValue)));
                }
                if (GetDefaultValueSql(false) != null)
                {
                    throw new InvalidOperationException(
                        RelationalStrings.ConflictingColumnServerGeneration(nameof(ValueGenerationStrategy), Property.Name, nameof(DefaultValueSql)));
                }
                if (GetComputedColumnSql(false) != null)
                {
                    throw new InvalidOperationException(
                        RelationalStrings.ConflictingColumnServerGeneration(nameof(ValueGenerationStrategy), Property.Name, nameof(ComputedColumnSql)));
                }
            }
            else if (value != null
                     && (!CanSetDefaultValue(null)
                         || !CanSetDefaultValueSql(null)
                         || !CanSetComputedColumnSql(null)))
            {
                return false;
            }

            return true;
        }

        protected override object GetDefaultValue(bool fallback)
        {
            if (fallback
                && ValueGenerationStrategy != null)
            {
                return null;
            }

            return base.GetDefaultValue(fallback);
        }

        protected override bool CanSetDefaultValue(object value)
        {
            if (ShouldThrowOnConflict)
            {
                if (ValueGenerationStrategy != null)
                {
                    throw new InvalidOperationException(
                        RelationalStrings.ConflictingColumnServerGeneration(nameof(DefaultValue), Property.Name, nameof(ValueGenerationStrategy)));
                }
            }
            else if (value != null
                     && !CanSetValueGenerationStrategy(null))
            {
                return false;
            }

            return base.CanSetDefaultValue(value);
        }

        protected override string GetDefaultValueSql(bool fallback)
        {
            if (fallback
                && ValueGenerationStrategy != null)
            {
                return null;
            }

            return base.GetDefaultValueSql(fallback);
        }

        protected override bool CanSetDefaultValueSql(string value)
        {
            if (ShouldThrowOnConflict)
            {
                if (ValueGenerationStrategy != null)
                {
                    throw new InvalidOperationException(
                        RelationalStrings.ConflictingColumnServerGeneration(nameof(DefaultValueSql), Property.Name, nameof(ValueGenerationStrategy)));
                }
            }
            else if (value != null
                     && !CanSetValueGenerationStrategy(null))
            {
                return false;
            }

            return base.CanSetDefaultValueSql(value);
        }

        protected override string GetComputedColumnSql(bool fallback)
        {
            if (fallback
                && ValueGenerationStrategy != null)
            {
                return null;
            }

            return base.GetComputedColumnSql(fallback);
        }

        protected override bool CanSetComputedColumnSql(string value)
        {
            if (ShouldThrowOnConflict)
            {
                if (ValueGenerationStrategy != null)
                {
                    throw new InvalidOperationException(
                        RelationalStrings.ConflictingColumnServerGeneration(nameof(ComputedColumnSql), Property.Name, nameof(ValueGenerationStrategy)));
                }
            }
            else if (value != null
                     && !CanSetValueGenerationStrategy(null))
            {
                return false;
            }

            return base.CanSetComputedColumnSql(value);
        }

        protected override void ClearAllServerGeneratedValues()
        {
            SetValueGenerationStrategy(null);

            base.ClearAllServerGeneratedValues();
        }

        private static bool IsCompatibleIdentityColumn(Type type)
            => type.IsInteger() || type == typeof(decimal);

        private static bool IsCompatibleSequenceHiLo(Type type) => type.IsInteger();
    }
}
