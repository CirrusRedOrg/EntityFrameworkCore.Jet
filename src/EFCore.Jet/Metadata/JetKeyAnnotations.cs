// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.Jet.Metadata
{
    public class JetKeyAnnotations : RelationalKeyAnnotations, IJetKeyAnnotations
    {
        public JetKeyAnnotations([NotNull] IKey key)
            : base(key)
        {
        }

        protected JetKeyAnnotations([NotNull] RelationalAnnotations annotations)
            : base(annotations)
        {
        }

        public virtual bool? IsClustered
        {
            get => (bool?)Annotations.Metadata[JetAnnotationNames.Clustered];
            set => SetIsClustered(value);
        }

        protected virtual bool SetIsClustered(bool? value)
            => Annotations.SetAnnotation(JetAnnotationNames.Clustered, value);
    }
}
