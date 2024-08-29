// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.FunctionalTests;

public class SerializationJetTest(F1JetFixture fixture) : SerializationTestBase<F1JetFixture>(fixture);
