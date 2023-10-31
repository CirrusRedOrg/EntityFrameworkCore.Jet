// ReSharper disable once CheckNamespace

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Microsoft.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
///     A convention that configures the OnDelete behavior for foreign keys on the join entity type for
///     self-referencing skip navigations
/// </summary>
public class JetOnDeleteConvention : CascadeDeleteConvention,
    ISkipNavigationForeignKeyChangedConvention,
    IEntityTypeAnnotationChangedConvention
{
    public JetOnDeleteConvention(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies)
        : base(dependencies)
    {
        RelationalDependencies = relationalDependencies;
    }

    /// <summary>
    ///     Relational provider-specific dependencies for this service.
    /// </summary>
    protected virtual RelationalConventionSetBuilderDependencies RelationalDependencies { get; }

    /// <inheritdoc />
    public virtual void ProcessSkipNavigationForeignKeyChanged(
        IConventionSkipNavigationBuilder skipNavigationBuilder,
        IConventionForeignKey? foreignKey,
        IConventionForeignKey? oldForeignKey,
        IConventionContext<IConventionForeignKey> context)
    {
        if (foreignKey is not null && foreignKey.IsInModel)
        {
            foreignKey.Builder.OnDelete(GetTargetDeleteBehavior(foreignKey));
        }
    }

    /// <inheritdoc />
    protected override DeleteBehavior GetTargetDeleteBehavior(IConventionForeignKey foreignKey)
    {
        var deleteBehavior = base.GetTargetDeleteBehavior(foreignKey);
        if (deleteBehavior != DeleteBehavior.Cascade)
        {
            return deleteBehavior;
        }

        return ProcessSkipNavigations(foreignKey.GetReferencingSkipNavigations()) ?? deleteBehavior;
    }

    private DeleteBehavior? ProcessSkipNavigations(IEnumerable<IConventionSkipNavigation> skipNavigations)
    {
        var skipNavigation = skipNavigations
            .FirstOrDefault(
                s => s.Inverse != null
                    && IsMappedToSameTable(s.DeclaringEntityType, s.TargetEntityType));

        if (skipNavigation != null)
        {
            var isFirstSkipNavigation = IsFirstSkipNavigation(skipNavigation);
            if (!isFirstSkipNavigation)
            {
                skipNavigation = skipNavigation.Inverse!;
            }

            var inverseSkipNavigation = skipNavigation.Inverse!;

            var deleteBehavior = DefaultDeleteBehavior(skipNavigation);
            var inverseDeleteBehavior = DefaultDeleteBehavior(inverseSkipNavigation);

            if (deleteBehavior == DeleteBehavior.Cascade
                && inverseDeleteBehavior == DeleteBehavior.Cascade
                && !(inverseSkipNavigation.ForeignKey!.GetDeleteBehaviorConfigurationSource() == ConfigurationSource.Explicit
                    && inverseSkipNavigation.ForeignKey!.DeleteBehavior != DeleteBehavior.Cascade))
            {
                deleteBehavior = DeleteBehavior.ClientCascade;
            }

            skipNavigation.ForeignKey!.Builder.OnDelete(deleteBehavior);
            inverseSkipNavigation.ForeignKey!.Builder.OnDelete(inverseDeleteBehavior);

            return isFirstSkipNavigation ? deleteBehavior : inverseDeleteBehavior;
        }

        return null;

        DeleteBehavior DefaultDeleteBehavior(IConventionSkipNavigation conventionSkipNavigation)
            => conventionSkipNavigation.ForeignKey!.IsRequired ? DeleteBehavior.Cascade : DeleteBehavior.ClientSetNull;

        bool IsMappedToSameTable(IConventionEntityType entityType1, IConventionEntityType entityType2)
        {
            var tableName1 = entityType1.GetTableName();
            var tableName2 = entityType2.GetTableName();

            return tableName1 != null
                && tableName2 != null
                && tableName1 == tableName2
                && entityType1.GetSchema() == entityType2.GetSchema();
        }

        bool IsFirstSkipNavigation(IConventionSkipNavigation navigation)
            => navigation.DeclaringEntityType != navigation.TargetEntityType
                ? string.Compare(navigation.DeclaringEntityType.Name, navigation.TargetEntityType.Name, StringComparison.Ordinal) < 0
                : string.Compare(navigation.Name, navigation.Inverse!.Name, StringComparison.Ordinal) < 0;
    }

    /// <inheritdoc />
    public virtual void ProcessEntityTypeAnnotationChanged(
        IConventionEntityTypeBuilder entityTypeBuilder,
        string name,
        IConventionAnnotation? annotation,
        IConventionAnnotation? oldAnnotation,
        IConventionContext<IConventionAnnotation> context)
    {
        if (name is RelationalAnnotationNames.TableName or RelationalAnnotationNames.Schema)
        {
            ProcessSkipNavigations(entityTypeBuilder.Metadata.GetDeclaredSkipNavigations());

            foreach (var foreignKey in entityTypeBuilder.Metadata.GetDeclaredForeignKeys())
            {
                var deleteBehavior = GetTargetDeleteBehavior(foreignKey);
                if (foreignKey.DeleteBehavior != deleteBehavior)
                {
                    foreignKey.Builder.OnDelete(deleteBehavior);
                }
            }
        }
    }
}
