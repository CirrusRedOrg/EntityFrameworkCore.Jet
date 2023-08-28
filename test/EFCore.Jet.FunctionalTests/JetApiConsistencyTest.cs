// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;
using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Jet.FunctionalTests;

public class JetApiConsistencyTest : ApiConsistencyTestBase<JetApiConsistencyTest.JetApiConsistencyFixture>
{
    public JetApiConsistencyTest(JetApiConsistencyFixture fixture)
        : base(fixture)
    {
    }

    protected override void AddServices(ServiceCollection serviceCollection)
        => serviceCollection.AddEntityFrameworkJet();

    protected override Assembly TargetAssembly
        => typeof(JetConnection).Assembly;

    public class JetApiConsistencyFixture : ApiConsistencyFixtureBase
    {
        public override HashSet<Type> FluentApiTypes { get; } = new()
        {
            typeof(JetDbContextOptionsBuilder),
            typeof(JetDbContextOptionsExtensions),
            typeof(JetMigrationBuilderExtensions),
            typeof(JetIndexBuilderExtensions),
            typeof(JetKeyBuilderExtensions),
            typeof(JetModelBuilderExtensions),
            typeof(JetPropertyBuilderExtensions),
            typeof(JetEntityTypeBuilderExtensions),
            typeof(JetServiceCollectionExtensions),
            typeof(JetDbFunctionsExtensions),
            typeof(OwnedNavigationTemporalPeriodPropertyBuilder),
            typeof(OwnedNavigationTemporalTableBuilder),
            typeof(OwnedNavigationTemporalTableBuilder<,>),
            typeof(TemporalPeriodPropertyBuilder),
            typeof(TemporalTableBuilder),
            typeof(TemporalTableBuilder<>)
        };

        public override
            List<(Type Type,
                Type ReadonlyExtensions,
                Type MutableExtensions,
                Type ConventionExtensions,
                Type ConventionBuilderExtensions,
                Type RuntimeExtensions)> MetadataExtensionTypes { get; }
            = new()
            {
                (
                    typeof(IReadOnlyModel),
                    typeof(JetModelExtensions),
                    typeof(JetModelExtensions),
                    typeof(JetModelExtensions),
                    typeof(JetModelBuilderExtensions),
                    null
                ),
                (
                    typeof(IReadOnlyEntityType),
                    typeof(JetEntityTypeExtensions),
                    typeof(JetEntityTypeExtensions),
                    typeof(JetEntityTypeExtensions),
                    typeof(JetEntityTypeBuilderExtensions),
                    null
                ),
                (
                    typeof(IReadOnlyKey),
                    typeof(JetKeyExtensions),
                    typeof(JetKeyExtensions),
                    typeof(JetKeyExtensions),
                    typeof(JetKeyBuilderExtensions),
                    null
                ),
                (
                    typeof(IReadOnlyProperty),
                    typeof(JetPropertyExtensions),
                    typeof(JetPropertyExtensions),
                    typeof(JetPropertyExtensions),
                    typeof(JetPropertyBuilderExtensions),
                    null
                ),
                (
                    typeof(IReadOnlyIndex),
                    typeof(JetIndexExtensions),
                    typeof(JetIndexExtensions),
                    typeof(JetIndexExtensions),
                    typeof(JetIndexBuilderExtensions),
                    null
                )
            };

        protected override void Initialize()
        {
            GenericFluentApiTypes.Add(typeof(TemporalTableBuilder), typeof(TemporalTableBuilder<>));
            GenericFluentApiTypes.Add(typeof(OwnedNavigationTemporalTableBuilder), typeof(OwnedNavigationTemporalTableBuilder<,>));

            MirrorTypes.Add(typeof(TemporalTableBuilder), typeof(OwnedNavigationTemporalTableBuilder));
            MirrorTypes.Add(typeof(TemporalTableBuilder<>), typeof(OwnedNavigationTemporalTableBuilder<,>));
            MirrorTypes.Add(typeof(TemporalPeriodPropertyBuilder), typeof(OwnedNavigationTemporalPeriodPropertyBuilder));

            base.Initialize();
        }
    }
}
