﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ReSharper disable InconsistentNaming

using Microsoft.EntityFrameworkCore.Query;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

public class TPCInheritanceQueryJetTest(TPCInheritanceQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
    : TPCInheritanceQueryJetTestBase<TPCInheritanceQueryJetFixture>(fixture, testOutputHelper);
