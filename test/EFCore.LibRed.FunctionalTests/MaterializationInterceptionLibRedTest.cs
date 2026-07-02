// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Collections.Generic;
using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using EntityFrameworkCore.LibRed.Infrastructure;
using EntityFrameworkCore.LibRed.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.LibRed.FunctionalTests;

public class MaterializationInterceptionLibRedTest(NonSharedFixture fixture) :
    MaterializationInterceptionTestBase<MaterializationInterceptionLibRedTest.LibRedLibraryContext>(fixture)
{
    public class LibRedLibraryContext(DbContextOptions options) : LibraryContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TestEntity30244>().OwnsMany(e => e.Settings, b => b.ToJson());
        }
    }

    protected override ITestStoreFactory TestStoreFactory
        => LibRedTestStoreFactory.Instance;
}
