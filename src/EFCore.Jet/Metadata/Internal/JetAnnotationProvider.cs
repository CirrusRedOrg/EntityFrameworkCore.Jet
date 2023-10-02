using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
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
        public override IEnumerable<IAnnotation> For(ITableIndex index, bool designTime)
        {
            if (!designTime)
            {
                yield break;
            }

            // Model validation ensures that these facets are the same on all mapped indexes
            var modelIndex = index.MappedIndexes.First();

            var table = index.Table;

            var includeProperties = modelIndex.GetJetIncludeProperties();
            if (includeProperties != null)
            {
                var includeColumns = (IReadOnlyList<string?>)includeProperties
                    .Select(
                        p => modelIndex.DeclaringEntityType.FindProperty(p)!
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
        public override IEnumerable<IAnnotation> For(IColumn column, bool designTime)
        {
            //Need to do this in both design and runtime
            /*if (!designTime)
            {
                yield break;
            }*/

            var table = StoreObjectIdentifier.Table(column.Table.Name, column.Table.Schema);
            var property = column.PropertyMappings.Where(
                    m =>
                        (m.TableMapping.IsSharedTablePrincipal ?? true) && m.TableMapping.EntityType == m.Property.DeclaringEntityType)
                .Select(m => m.Property)
                .FirstOrDefault(
                    p => p.GetValueGenerationStrategy(table)
                        == JetValueGenerationStrategy.IdentityColumn);
            if (property != null)
            {
                var seed = property.GetJetIdentitySeed(table);
                var increment = property.GetJetIdentityIncrement(table);

                yield return new Annotation(
                    JetAnnotationNames.Identity,
                    string.Format(CultureInfo.InvariantCulture, "{0}, {1}", seed ?? 1, increment ?? 1));
            }
            else
            {
                property = column.PropertyMappings.First().Property;
                // Only return auto increment for integer single column primary key
                var primaryKey = property.DeclaringEntityType.FindPrimaryKey();
                if (primaryKey != null
                    && primaryKey.Properties.Count == 1
                    && primaryKey.Properties[0] == property
                    && property.ValueGenerated == ValueGenerated.OnAdd
                    && property.ClrType.UnwrapNullableType().IsInteger()
                    && !HasConverter(property))
                {
                    yield return new Annotation(
                        JetAnnotationNames.Identity,
                        string.Format(CultureInfo.InvariantCulture, "{0}, {1}", 1, 1));
                }
            }
        }

        public override IEnumerable<IAnnotation> For(IForeignKeyConstraint foreignKey, bool designTime)
        {
            var table = StoreObjectIdentifier.Table(foreignKey.Table.Name, foreignKey.Table.Schema);
            foreach (var fk in foreignKey.MappedForeignKeys)
            {
                if (fk.FindAnnotation(JetAnnotationNames.Prefix + "MatchSimple") != null)
                {
                    yield return new Annotation(JetAnnotationNames.Prefix + "MatchSimple", "MatchSimple");
                }
            }
        }


        private static bool HasConverter(IProperty property)
            => (property.GetValueConverter() ?? property.FindTypeMapping()?.Converter) != null;
    }
}