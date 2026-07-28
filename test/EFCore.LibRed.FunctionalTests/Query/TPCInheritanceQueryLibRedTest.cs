// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ReSharper disable InconsistentNaming

using Microsoft.EntityFrameworkCore.Query;
using Xunit.Abstractions;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query;

public class TPCInheritanceQueryLibRedTest(TPCInheritanceQueryLibRedFixture fixture, ITestOutputHelper testOutputHelper)
    : TPCInheritanceQueryLibRedTestBase<TPCInheritanceQueryLibRedFixture>(fixture, testOutputHelper);
