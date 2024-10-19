// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Metadata;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore.Metadata.Conventions
{
    /// <summary>
    ///     A convention that configures the default model <see cref="JetValueGenerationStrategy" /> as
    ///     <see cref="JetValueGenerationStrategy.IdentityColumn" />.
    /// </summary>
    /// <remarks>
    ///     Creates a new instance of <see cref="JetValueGenerationStrategyConvention" />.
    /// </remarks>
    /// <param name="dependencies"> Parameter object containing dependencies for this convention. </param>
    /// <param name="relationalDependencies">  Parameter object containing relational dependencies for this convention. </param>
    public class JetValueGenerationStrategyConvention(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies) : IModelInitializedConvention, IModelFinalizingConvention
    {
        protected virtual ProviderConventionSetBuilderDependencies Dependencies { get; } = dependencies;

        protected virtual RelationalConventionSetBuilderDependencies RelationalDependencies { get; } = relationalDependencies;

        /// <summary>
        ///     Called after a model is initialized.
        /// </summary>
        /// <param name="modelBuilder"> The builder for the model. </param>
        /// <param name="context"> Additional information associated with convention execution. </param>
        public virtual void ProcessModelInitialized(
            IConventionModelBuilder modelBuilder,
            IConventionContext<IConventionModelBuilder> context)
        {
            modelBuilder.HasValueGenerationStrategy(JetValueGenerationStrategy.IdentityColumn);
        }

        /// <inheritdoc />
        public virtual void ProcessModelFinalizing(
            IConventionModelBuilder modelBuilder,
            IConventionContext<IConventionModelBuilder> context)
        {
            foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
            {
                foreach (var property in entityType.GetDeclaredProperties())
                {
                    JetValueGenerationStrategy? strategy = null;
                    var declaringTable = property.GetMappedStoreObjects(StoreObjectType.Table).FirstOrDefault();
                    if (declaringTable.Name != null!)
                    {
                        strategy = property.GetValueGenerationStrategy(declaringTable, Dependencies.TypeMappingSource);
                        if (strategy == JetValueGenerationStrategy.None
                            && !IsStrategyNoneNeeded(property, declaringTable))
                        {
                            strategy = null;
                        }
                    }
                    else
                    {
                        var declaringView = property.GetMappedStoreObjects(StoreObjectType.View).FirstOrDefault();
                        if (declaringView.Name != null!)
                        {
                            strategy = property.GetValueGenerationStrategy(declaringView, Dependencies.TypeMappingSource);
                            if (strategy == JetValueGenerationStrategy.None
                                && !IsStrategyNoneNeeded(property, declaringView))
                            {
                                strategy = null;
                            }
                        }
                    }

                    // Needed for the annotation to show up in the model snapshot
                    if (strategy != null
                        && declaringTable.Name != null)
                    {
                        property.Builder.HasValueGenerationStrategy(strategy);
                    }
                }
            }

            static bool IsStrategyNoneNeeded(IReadOnlyProperty property, StoreObjectIdentifier storeObject)
            {
                if (property.ValueGenerated == ValueGenerated.OnAdd
                    && property.TryGetDefaultValue(storeObject, out _) == false
                    && property.GetDefaultValueSql(storeObject) == null
                    && property.GetComputedColumnSql(storeObject) == null
                    && property.DeclaringType.Model.GetValueGenerationStrategy() == JetValueGenerationStrategy.IdentityColumn)
                {
                    var providerClrType = (property.GetValueConverter() ?? property.FindRelationalTypeMapping(storeObject)?.Converter)
                        ?.ProviderClrType.UnwrapNullableType();

                    return providerClrType != null
                        && (providerClrType.IsInteger() || providerClrType == typeof(decimal));
                }

                return false;
            }
        }
    }
}