// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore.Metadata.Conventions
{
    /// <summary>
    ///     A convention that configures store value generation as <see cref="ValueGenerated.OnAdd" /> on properties that are
    ///     part of the primary key and not part of any foreign keys, were configured to have a database default value
    ///     or were configured to use a <see cref="JetValueGenerationStrategy" />.
    ///     It also configures properties as <see cref="ValueGenerated.OnAddOrUpdate" /> if they were configured as computed columns.
    /// </summary>
    /// <remarks>
    ///     Creates a new instance of <see cref="JetValueGenerationConvention" />.
    /// </remarks>
    /// <param name="dependencies"> Parameter object containing dependencies for this convention. </param>
    /// <param name="relationalDependencies">  Parameter object containing relational dependencies for this convention. </param>
    public class JetValueGenerationConvention(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies) : RelationalValueGenerationConvention(dependencies, relationalDependencies)
    {

        /// <summary>
        ///     Called after an annotation is changed on a property.
        /// </summary>
        /// <param name="propertyBuilder"> The builder for the property. </param>
        /// <param name="name"> The annotation name. </param>
        /// <param name="annotation"> The new annotation. </param>
        /// <param name="oldAnnotation"> The old annotation.  </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public override void ProcessPropertyAnnotationChanged(
            IConventionPropertyBuilder propertyBuilder,
            string name,
            IConventionAnnotation? annotation,
            IConventionAnnotation? oldAnnotation,
            IConventionContext<IConventionAnnotation> context)
        {
            if (name == JetAnnotationNames.ValueGenerationStrategy)
            {
                propertyBuilder.ValueGenerated(GetValueGenerated(propertyBuilder.Metadata));
                return;
            }

            base.ProcessPropertyAnnotationChanged(propertyBuilder, name, annotation, oldAnnotation, context);
        }

        /// <summary>
        ///     Returns the store value generation strategy to set for the given property.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <returns> The store value generation strategy to set for the given property. </returns>
        protected override ValueGenerated? GetValueGenerated(IConventionProperty property)
        {
            if (property.DeclaringType.IsMappedToJson()
#pragma warning disable EF1001 // Internal EF Core API usage.
                && property.IsOrdinalKeyProperty()
#pragma warning restore EF1001 // Internal EF Core API usage.
                && (property.DeclaringType as IReadOnlyEntityType)?.FindOwnership()!.IsUnique == false)
            {
                return ValueGenerated.OnAdd;
            }
            var declaringTable = property.GetMappedStoreObjects(StoreObjectType.Table).FirstOrDefault();
            if (declaringTable.Name == null)
            {
                return null;
            }

            if (!MappingStrategyAllowsValueGeneration(property, property.DeclaringType.GetMappingStrategy()))
            {
                return null;
            }

            // If the first mapping can be value generated then we'll consider all mappings to be value generated
            // as this is a client-side configuration and can't be specified per-table.
            return GetValueGenerated(property, declaringTable);
        }

        /// <summary>
        ///     Returns the store value generation strategy to set for the given property.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="storeObject">The identifier of the store object.</param>
        /// <returns>The store value generation strategy to set for the given property.</returns>
        public new static ValueGenerated? GetValueGenerated(IReadOnlyProperty property, in StoreObjectIdentifier storeObject)
            => RelationalValueGenerationConvention.GetValueGenerated(property, storeObject)
                      ?? (property.GetValueGenerationStrategy(storeObject) != JetValueGenerationStrategy.None
                          ? ValueGenerated.OnAdd
                          : null);

    }
}