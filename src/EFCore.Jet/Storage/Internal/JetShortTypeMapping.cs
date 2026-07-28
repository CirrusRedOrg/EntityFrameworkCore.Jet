// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Data;

namespace EntityFrameworkCore.Jet.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class JetShortTypeMapping : ShortTypeMapping
{
    public static new JetShortTypeMapping Default { get; } = new("smallint", System.Data.DbType.Int16);

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public JetShortTypeMapping(
        string storeType,
        DbType? dbType = System.Data.DbType.Int16)
        : base(storeType, dbType)
    {
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    protected JetShortTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <summary>
    ///     Creates a copy of this mapping.
    /// </summary>
    /// <param name="parameters">The parameters for this mapping.</param>
    /// <returns>The newly created mapping.</returns>
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new JetShortTypeMapping(parameters);

    /// <summary>
    ///     A bare integer literal is Long/Integer to Jet, so a short (smallint) constant must be wrapped in
    ///     CINT (VBA CInt → 16-bit Integer) — mirroring <see cref="JetByteTypeMapping"/>'s CBYTE — otherwise the
    ///     reader gets an Int32 and GetInt16 throws.
    /// </summary>
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        return $"CINT({base.GenerateNonNullSqlLiteral(value)})";
    }
}
