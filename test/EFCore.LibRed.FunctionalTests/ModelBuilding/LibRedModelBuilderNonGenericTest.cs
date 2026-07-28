// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ReSharper disable InconsistentNaming

using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Features;
using System;

namespace EntityFrameworkCore.LibRed.FunctionalTests.ModelBuilding;

public class LibRedModelBuilderNonGenericTest : LibRedModelBuilderTestBase
{
    public class LibRedNonGenericNonRelationship(LibRedModelBuilderFixture fixture) : LibRedNonRelationship(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
            => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedNonGenericComplexType(LibRedModelBuilderFixture fixture) : LibRedComplexType(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
            => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedNonGenericComplexCollection(LibRedModelBuilderFixture fixture) : LibRedComplexCollection(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
            => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedNonGenericInheritance(LibRedModelBuilderFixture fixture) : LibRedInheritance(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
            => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedNonGenericOneToMany(LibRedModelBuilderFixture fixture) : LibRedOneToMany(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
            => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedNonGenericManyToOne(LibRedModelBuilderFixture fixture) : LibRedManyToOne(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
            => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedNonGenericOneToOne(LibRedModelBuilderFixture fixture) : LibRedOneToOne(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
            => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedNonGenericManyToMany(LibRedModelBuilderFixture fixture) : LibRedManyToMany(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
            => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedNonGenericOwnedTypes(LibRedModelBuilderFixture fixture) : LibRedOwnedTypes(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
            => new NonGenericTestModelBuilder(Fixture, configure);
    }
}
