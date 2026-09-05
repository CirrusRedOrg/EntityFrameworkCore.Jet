// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.Jet.Infrastructure.Internal;
using EntityFrameworkCore.Jet.Query.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Collections.Generic;
using System.Reflection;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;

public class JetPrecompiledQueryTestHelpers : PrecompiledQueryTestHelpers
{
    public static JetPrecompiledQueryTestHelpers Instance = new();

    protected override IEnumerable<MetadataReference> BuildProviderMetadataReferences()
    {
        yield return MetadataReference.CreateFromFile(typeof(JetOptionsExtension).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(JetSqlTranslatingExpressionVisitor).Assembly.Location);
        yield return MetadataReference.CreateFromFile(Assembly.GetExecutingAssembly().Location);
    }
}
