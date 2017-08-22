// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EntityFrameworkCore.Jet.Migrations.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetMigrationsAnnotationProvider : MigrationsAnnotationProvider
    {
        /// <summary>
        ///     Initializes a new instance of this class.
        /// </summary>
        /// <param name="dependencies"> Parameter object containing dependencies for this service. </param>
        public JetMigrationsAnnotationProvider([NotNull] MigrationsAnnotationProviderDependencies dependencies)
            : base(dependencies)
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override IEnumerable<IAnnotation> For(IModel model) => ForRemove(model);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override IEnumerable<IAnnotation> For(IEntityType entityType) => ForRemove(entityType);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override IEnumerable<IAnnotation> For(IKey key)
        {
            var isClustered = key.Jet().IsClustered;
            if (isClustered.HasValue)
            {
                yield return new Annotation(
                    JetAnnotationNames.Clustered,
                    isClustered.Value);
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override IEnumerable<IAnnotation> For(IIndex index)
        {
            var isClustered = index.Jet().IsClustered;
            if (isClustered.HasValue)
            {
                yield return new Annotation(
                    JetAnnotationNames.Clustered,
                    isClustered.Value);
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override IEnumerable<IAnnotation> For(IProperty property)
        {
            if (property.Jet().ValueGenerationStrategy == JetValueGenerationStrategy.IdentityColumn)
            {
                yield return new Annotation(
                    JetAnnotationNames.ValueGenerationStrategy,
                    JetValueGenerationStrategy.IdentityColumn);
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override IEnumerable<IAnnotation> ForRemove(IModel model)
        {
            if (model.GetEntityTypes().Any(e => e.BaseType == null && e.Jet().IsMemoryOptimized))
            {
                yield return new Annotation(
                    JetAnnotationNames.MemoryOptimized,
                    true);
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override IEnumerable<IAnnotation> ForRemove(IEntityType entityType)
        {
            if (IsMemoryOptimized(entityType))
            {
                yield return new Annotation(
                    JetAnnotationNames.MemoryOptimized,
                    true);
            }
        }

        private static bool IsMemoryOptimized(IEntityType entityType)
            => entityType.GetAllBaseTypesInclusive().Any(t => t.Jet().IsMemoryOptimized);
    }
}
