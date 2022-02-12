using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Jet.Metadata.Internal
{
    /// <summary>
    ///     <para>
    ///         This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///         the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///         any release. You should only use it directly in your code with extreme caution and knowing that
    ///         doing so can result in application failures when updating to a new Entity Framework Core release.
    ///     </para>
    ///     <para>
    ///         The service lifetime is <see cref="ServiceLifetime.Singleton" />. This means a single instance
    ///         is used by many <see cref="DbContext" /> instances. The implementation must be thread-safe.
    ///         This service cannot depend on services registered as <see cref="ServiceLifetime.Scoped" />.
    ///     </para>
    /// </summary>
    public class JetAnnotationProvider : RelationalAnnotationProvider
    {
        /// <summary>
        ///     Initializes a new instance of this class.
        /// </summary>
        /// <param name="dependencies"> Parameter object containing dependencies for this service. </param>
        public JetAnnotationProvider([NotNull] RelationalAnnotationProviderDependencies dependencies)
            : base(dependencies)
        {
        }
        
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override IEnumerable<IAnnotation> For(ITableIndex index)
        {
            // Model validation ensures that these facets are the same on all mapped indexes
            var modelIndex = index.MappedIndexes.First();

            var table = index.Table;

            var includeProperties = modelIndex.GetIncludeProperties();
            if (includeProperties != null)
            {
                var includeColumns = (IReadOnlyList<string>)includeProperties
                    .Select(
                        p => modelIndex.DeclaringEntityType.FindProperty(p)
                            .GetColumnName(StoreObjectIdentifier.Table(table.Name, table.Schema)))
                    .ToArray();

                yield return new Annotation(
                    JetAnnotationNames.Include,
                    includeColumns);
            }
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override IEnumerable<IAnnotation> For(IColumn column)
        {
            var table = StoreObjectIdentifier.Table(column.Table.Name, column.Table.Schema);
            var property = column.PropertyMappings.Where(
                    m =>
                        m.TableMapping.IsSharedTablePrincipal && m.TableMapping.EntityType == m.Property.DeclaringEntityType)
                .Select(m => m.Property)
                .FirstOrDefault(
                    p => p.GetValueGenerationStrategy(table)
                        == JetValueGenerationStrategy.IdentityColumn);
            if (property != null)
            {
                var seed = property.GetIdentitySeed(table);
                var increment = property.GetIdentityIncrement(table);

                yield return new Annotation(
                    JetAnnotationNames.Identity,
                    string.Format(CultureInfo.InvariantCulture, "{0}, {1}", seed ?? 1, increment ?? 1));
            }
        }
    }
}