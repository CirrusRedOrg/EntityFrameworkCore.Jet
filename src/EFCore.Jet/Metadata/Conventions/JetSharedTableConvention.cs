// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.



// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
///     A convention that manipulates names of database objects for entity types that share a table to avoid clashes.
/// </summary>
/// <remarks>
///     Creates a new instance of <see cref="JetSharedTableConvention" />.
/// </remarks>
/// <param name="dependencies">Parameter object containing dependencies for this convention.</param>
/// <param name="relationalDependencies"> Parameter object containing relational dependencies for this convention.</param>
public class JetSharedTableConvention(
    ProviderConventionSetBuilderDependencies dependencies,
    RelationalConventionSetBuilderDependencies relationalDependencies) : SharedTableConvention(dependencies, relationalDependencies)
{

    /// <inheritdoc />
    protected override bool IndexesUniqueAcrossTables
        => false;
}
