// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.Query.Relationships.Projection;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Relationships.Projection;

// Only adding NoTracking version - no point to do both and most of the tests don't work in tracking (projecting without owner)
public class OwnedJsonTypeNoTrackingProjectionJetTest
    : ProjectionTestBase<OwnedJsonTypeRelationshipsJetFixture>
{
    public OwnedJsonTypeNoTrackingProjectionJetTest(OwnedJsonTypeRelationshipsJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());
}
