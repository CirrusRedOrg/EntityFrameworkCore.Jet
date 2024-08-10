using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Jet.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class JetQueryCompilationContextFactory : IQueryCompilationContextFactory
{
    private readonly IJetRelationalConnection _jetConnection;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public JetQueryCompilationContextFactory(
        QueryCompilationContextDependencies dependencies,
        RelationalQueryCompilationContextDependencies relationalDependencies,
        IJetRelationalConnection jetConnection)
    {
        Dependencies = dependencies;
        RelationalDependencies = relationalDependencies;
        _jetConnection = jetConnection;
    }

    /// <summary>
    ///     Dependencies for this service.
    /// </summary>
    protected virtual QueryCompilationContextDependencies Dependencies { get; }

    /// <summary>
    ///     Relational provider-specific dependencies for this service.
    /// </summary>
    protected virtual RelationalQueryCompilationContextDependencies RelationalDependencies { get; }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual QueryCompilationContext Create(bool async)
        => new JetQueryCompilationContext(
            Dependencies, RelationalDependencies, async);

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual QueryCompilationContext CreatePrecompiled(bool async, IReadOnlySet<string> nonNullableReferenceTypeParameters)
        => new JetQueryCompilationContext(
            Dependencies, RelationalDependencies, async, precompiling: true,
            nonNullableReferenceTypeParameters);
}
