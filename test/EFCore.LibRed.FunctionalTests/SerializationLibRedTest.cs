// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.LibRed.FunctionalTests;

public class SerializationLibRedTest(F1LibRedFixture fixture) : SerializationTestBase<F1LibRedFixture>(fixture);
