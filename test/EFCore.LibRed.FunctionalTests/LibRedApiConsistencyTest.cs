// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;
using EntityFrameworkCore.LibRed.Infrastructure;
using EntityFrameworkCore.LibRed.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.LibRed.FunctionalTests;

#nullable disable
public class LibRedApiConsistencyTest(LibRedApiConsistencyTest.LibRedApiConsistencyFixture fixture)
    : ApiConsistencyTestBase<LibRedApiConsistencyTest.LibRedApiConsistencyFixture>(fixture)
{
    protected override void AddServices(ServiceCollection serviceCollection)
        => serviceCollection.AddEntityFrameworkLibRed();

    protected override Assembly TargetAssembly
        => typeof(LibRedRelationalConnection).Assembly;

    public class LibRedApiConsistencyFixture : ApiConsistencyFixtureBase
    {
        public override HashSet<Type> FluentApiTypes { get; } =
        [
            typeof(LibRedDbContextOptionsBuilder),
            //typeof(JetDbContextOptionsExtensions),
            //typeof(JetMigrationBuilderExtensions),
            //typeof(JetIndexBuilderExtensions),
            //typeof(JetKeyBuilderExtensions),
            typeof(JetModelBuilderExtensions),
            typeof(JetPropertyBuilderExtensions),
            //typeof(JetPrimitiveCollectionBuilderExtensions),
            //typeof(JetComplexTypePrimitiveCollectionBuilderExtensions),
            //typeof(JetEntityTypeBuilderExtensions),
            typeof(JetServiceCollectionExtensions),
            typeof(JetDbFunctionsExtensions),
            //typeof(JetTableBuilderExtensions)
        ];

        public override
            Dictionary<Type,
                (Type ReadonlyExtensions,
                Type MutableExtensions,
                Type ConventionExtensions,
                Type ConventionBuilderExtensions,
                Type RuntimeExtensions)> MetadataExtensionTypes
        { get; }
            = new()
            {
                {
                    typeof(IReadOnlyModel), (
                        typeof(JetModelExtensions),
                        typeof(JetModelExtensions),
                        typeof(JetModelExtensions),
                        typeof(JetModelBuilderExtensions),
                        null
                    )
                },
                {
                    typeof(IReadOnlyEntityType), (
                        null,//typeof(JetEntityTypeExtensions),
                        null,//typeof(JetEntityTypeExtensions),
                        null,//typeof(JetEntityTypeExtensions),
                        null,//typeof(JetEntityTypeBuilderExtensions),
                        null
                    )
                },
                {
                    typeof(IReadOnlyKey), (
                        null,//typeof(JetKeyExtensions),
                        null,//typeof(JetKeyExtensions),
                        null,//typeof(JetKeyExtensions),
                        null,//typeof(JetKeyBuilderExtensions),
                        null
                    )
                },
                {
                    typeof(IReadOnlyProperty), (
                        typeof(JetPropertyExtensions),
                        typeof(JetPropertyExtensions),
                        typeof(JetPropertyExtensions),
                        typeof(JetPropertyBuilderExtensions),
                        null
                    )
                },
                {
                    typeof(IReadOnlyIndex), (
                        typeof(JetIndexExtensions),
                        typeof(JetIndexExtensions),
                        typeof(JetIndexExtensions),
                        null,//typeof(JetIndexBuilderExtensions),
                        null
                    )
                },
                {
                    typeof(IReadOnlyElementType), (
                        null,
                        null,
                        null,
                        null,//typeof(JetEntityTypeBuilderExtensions),
                        null
                    )
                }
            };

        protected override void Initialize()
        {
            /*MirrorTypes.Add(typeof(JetPropertyBuilderExtensions), typeof(JetComplexTypePropertyBuilderExtensions));
            MirrorTypes.Add(typeof(JetPrimitiveCollectionBuilderExtensions), typeof(JetPropertyBuilderExtensions));
            MirrorTypes.Add(
                typeof(JetComplexTypePrimitiveCollectionBuilderExtensions), typeof(JetComplexTypePropertyBuilderExtensions));
            */
            base.Initialize();
        }
    }
}
