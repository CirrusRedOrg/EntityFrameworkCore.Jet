// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.LibRed.Infrastructure.Internal;

namespace EntityFrameworkCore.LibRed.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </remarks>
public class LibRedParameterBasedSqlProcessorFactory(
    RelationalParameterBasedSqlProcessorDependencies dependencies,
    ILibRedOptions options) : IRelationalParameterBasedSqlProcessorFactory
{

    /// <summary>
    ///     Relational provider-specific dependencies for this service.
    /// </summary>
    protected virtual RelationalParameterBasedSqlProcessorDependencies Dependencies { get; } = dependencies;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public RelationalParameterBasedSqlProcessor Create(RelationalParameterBasedSqlProcessorParameters parameters)
    => new LibRedParameterBasedSqlProcessor(Dependencies, parameters, options.SqlMode);
}
