// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using System.Text;
using EntityFrameworkCore.Jet.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Metadata.Conventions.Internal
{
    public class JetIndexConvention :
        IIndexAddedConvention,
        IIndexUniquenessChangedConvention,
        IIndexAnnotationChangedConvention,
        IPropertyNullabilityChangedConvention,
        IPropertyAnnotationChangedConvention
    {
        private readonly ISqlGenerationHelper _sqlGenerationHelper;

        public JetIndexConvention([NotNull] ISqlGenerationHelper sqlGenerationHelper)
        {
            _sqlGenerationHelper = sqlGenerationHelper;
        }

        InternalIndexBuilder IIndexAddedConvention.Apply(InternalIndexBuilder indexBuilder)
            => SetIndexFilter(indexBuilder);

        bool IIndexUniquenessChangedConvention.Apply(InternalIndexBuilder indexBuilder)
        {
            SetIndexFilter(indexBuilder);
            return true;
        }

        public virtual bool Apply(InternalPropertyBuilder propertyBuilder)
        {
            foreach (var index in propertyBuilder.Metadata.GetContainingIndexes())
            {
                SetIndexFilter(index.Builder);
            }
            return true;
        }

        public virtual Annotation Apply(InternalIndexBuilder indexBuilder, string name, Annotation annotation, Annotation oldAnnotation)
        {
            if (name == JetAnnotationNames.Clustered)
            {
                SetIndexFilter(indexBuilder);
            }

            return annotation;
        }

        public virtual Annotation Apply(InternalPropertyBuilder propertyBuilder, string name, Annotation annotation, Annotation oldAnnotation)
        {
            if (name == RelationalAnnotationNames.ColumnName)
            {
                foreach (var index in propertyBuilder.Metadata.GetContainingIndexes())
                {
                    SetIndexFilter(index.Builder, columnNameChanged: true);
                }
            }
            return annotation;
        }

        private InternalIndexBuilder SetIndexFilter(InternalIndexBuilder indexBuilder, bool columnNameChanged = false)
        {
            return indexBuilder;
        }

    }
}
