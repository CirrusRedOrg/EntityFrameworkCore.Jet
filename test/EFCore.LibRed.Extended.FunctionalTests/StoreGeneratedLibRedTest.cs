// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests
{
    public class StoreGeneratedLibRedTest(StoreGeneratedLibRedTest.StoreGeneratedLibRedFixture fixture)
        : StoreGeneratedLibRedTestBase<StoreGeneratedLibRedTest.StoreGeneratedLibRedFixture>(fixture)
    {
        public class StoreGeneratedLibRedFixture : StoreGeneratedLibRedFixtureBase
        {
            protected override string StoreName
                => "StoreGeneratedTest";
        }
    }
}
