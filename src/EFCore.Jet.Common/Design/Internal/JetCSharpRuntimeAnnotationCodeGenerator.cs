using EntityFrameworkCore.Jet.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Design.Internal;

namespace EntityFrameworkCore.Jet.Design.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
#pragma warning disable EF1001 // Internal EF Core API usage.
public class JetCSharpRuntimeAnnotationCodeGenerator : RelationalCSharpRuntimeAnnotationCodeGenerator
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public JetCSharpRuntimeAnnotationCodeGenerator(
        CSharpRuntimeAnnotationCodeGeneratorDependencies dependencies,
        RelationalCSharpRuntimeAnnotationCodeGeneratorDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    /// <inheritdoc />
    public override void Generate(IModel model, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        if (!parameters.IsRuntime)
        {
            var annotations = parameters.Annotations;
        }

        base.Generate(model, parameters);
    }

    /// <inheritdoc />
    public override void Generate(IRelationalModel model, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        if (!parameters.IsRuntime)
        {
            var annotations = parameters.Annotations;
        }

        base.Generate(model, parameters);
    }

    /// <inheritdoc />
    public override void Generate(IProperty property, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        /*if (!parameters.IsRuntime)
        {
            var annotations = parameters.Annotations;

            if (!annotations.ContainsKey(JetAnnotationNames.ValueGenerationStrategy))
            {
                annotations[JetAnnotationNames.ValueGenerationStrategy] = property.GetValueGenerationStrategy();
            }
        }*/

        base.Generate(property, parameters);
    }

    /// <inheritdoc />
    public override void Generate(IColumn column, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        if (!parameters.IsRuntime)
        {
            var annotations = parameters.Annotations;
        }

        base.Generate(column, parameters);
    }

    /// <inheritdoc />
    public override void Generate(IIndex index, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        if (!parameters.IsRuntime)
        {
            var annotations = parameters.Annotations;
            annotations.Remove(JetAnnotationNames.Include);
        }

        base.Generate(index, parameters);
    }

    /// <inheritdoc />
    public override void Generate(ITableIndex index, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        if (!parameters.IsRuntime)
        {
            var annotations = parameters.Annotations;
            annotations.Remove(JetAnnotationNames.Include);
        }

        base.Generate(index, parameters);
    }

    /// <inheritdoc />
    public override void Generate(IKey key, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        if (!parameters.IsRuntime)
        {
            var annotations = parameters.Annotations;
        }

        base.Generate(key, parameters);
    }

    /// <inheritdoc />
    public override void Generate(IUniqueConstraint uniqueConstraint, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        if (!parameters.IsRuntime)
        {
            var annotations = parameters.Annotations;
        }

        base.Generate(uniqueConstraint, parameters);
    }

    /// <inheritdoc />
    public override void Generate(IEntityType entityType, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        if (!parameters.IsRuntime)
        {
            var annotations = parameters.Annotations;
        }

        base.Generate(entityType, parameters);
    }

    /// <inheritdoc />
    public override void Generate(ITable table, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        if (!parameters.IsRuntime)
        {
            var annotations = parameters.Annotations;
        }

        base.Generate(table, parameters);
    }

    /// <inheritdoc />
    public override void Generate(IRelationalPropertyOverrides overrides, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        if (!parameters.IsRuntime)
        {
            var annotations = parameters.Annotations;
        }

        base.Generate(overrides, parameters);
    }
}
