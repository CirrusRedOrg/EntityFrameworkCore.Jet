// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.Jet.Metadata
{
    public class JetEntityTypeAnnotations : RelationalEntityTypeAnnotations, IJetEntityTypeAnnotations
    {
        public JetEntityTypeAnnotations([NotNull] IEntityType entityType)
            : base(entityType)
        {
        }

        public JetEntityTypeAnnotations([NotNull] RelationalAnnotations annotations)
            : base(annotations)
        {
        }

        public virtual bool IsMemoryOptimized
        {
            get => Annotations.Metadata[JetAnnotationNames.MemoryOptimized] as bool? ?? false;
            set => SetIsMemoryOptimized(value);
        }

        protected virtual bool SetIsMemoryOptimized(bool value)
            => Annotations.SetAnnotation(JetAnnotationNames.MemoryOptimized, value);
    }
}
