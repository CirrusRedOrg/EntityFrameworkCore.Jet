// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Reflection;
using EntityFrameworkCore.Jet.Query.Internal;
using EntityFrameworkCore.LibRed.Infrastructure.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;

public class LibRedPrecompiledQueryTestHelpers : PrecompiledQueryTestHelpers
{
    public static LibRedPrecompiledQueryTestHelpers Instance = new();

    protected override IEnumerable<MetadataReference> BuildProviderMetadataReferences()
    {
        yield return MetadataReference.CreateFromFile(typeof(LibRedOptionsExtension).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(JetSqlTranslatingExpressionVisitor).Assembly.Location);
        yield return MetadataReference.CreateFromFile(Assembly.GetExecutingAssembly().Location);
    }
}
