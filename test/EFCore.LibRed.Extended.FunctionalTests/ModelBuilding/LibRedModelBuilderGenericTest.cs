// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ReSharper disable InconsistentNaming

using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Features;
using System;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.ModelBuilding;

public class LibRedModelBuilderGenericTest : LibRedModelBuilderTestBase
{
    public class LibRedGenericNonRelationship(LibRedModelBuilderFixture fixture) : LibRedNonRelationship(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedGenericComplexType(LibRedModelBuilderFixture fixture) : LibRedComplexType(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedGenericComplexCollection(LibRedModelBuilderFixture fixture) : LibRedComplexCollection(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedGenericInheritance(LibRedModelBuilderFixture fixture) : LibRedInheritance(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedGenericOneToMany(LibRedModelBuilderFixture fixture) : LibRedOneToMany(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedGenericManyToOne(LibRedModelBuilderFixture fixture) : LibRedManyToOne(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedGenericOneToOne(LibRedModelBuilderFixture fixture) : LibRedOneToOne(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedGenericManyToMany(LibRedModelBuilderFixture fixture) : LibRedManyToMany(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }

    public class LibRedGenericOwnedTypes(LibRedModelBuilderFixture fixture) : LibRedOwnedTypes(fixture)
    {
        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure)
            => new GenericTestModelBuilder(Fixture, configure);
    }
}
