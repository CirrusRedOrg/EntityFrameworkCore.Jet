// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class StoreGeneratedJetTest : StoreGeneratedJetTestBase<StoreGeneratedJetTest.StoreGeneratedJetFixture>
    {
        public StoreGeneratedJetTest(StoreGeneratedJetFixture fixture)
            : base(fixture)
        {
        }

        public class StoreGeneratedJetFixture : StoreGeneratedJetFixtureBase
        {
            protected override string StoreName
                => "StoreGeneratedTest";
        }
    }
}
