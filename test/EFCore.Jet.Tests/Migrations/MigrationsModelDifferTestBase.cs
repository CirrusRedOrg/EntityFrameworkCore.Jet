// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using EntityFrameworkCore.Jet.Tests.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Update.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Jet.Tests.Migrations
{
    public abstract class MigrationsModelDifferTestBase
    {
        protected void Execute(
            Action<ModelBuilder> buildSourceAction,
            Action<ModelBuilder> buildTargetAction,
            Action<IReadOnlyList<MigrationOperation>> assertAction)
            => Execute(m => { }, buildSourceAction, buildTargetAction, assertAction, null);

        protected void Execute(
            Action<ModelBuilder> buildCommonAction,
            Action<ModelBuilder> buildSourceAction,
            Action<ModelBuilder> buildTargetAction,
            Action<IReadOnlyList<MigrationOperation>> assertAction)
            => Execute(buildCommonAction, buildSourceAction, buildTargetAction, assertAction, null);

        protected void Execute(
            Action<ModelBuilder> buildCommonAction,
            Action<ModelBuilder> buildSourceAction,
            Action<ModelBuilder> buildTargetAction,
            Action<IReadOnlyList<MigrationOperation>> assertActionUp,
            Action<IReadOnlyList<MigrationOperation>> assertActionDown)
        {
            var sourceModelBuilder = CreateModelBuilder();
            buildCommonAction(sourceModelBuilder);
            buildSourceAction(sourceModelBuilder);
            sourceModelBuilder.GetInfrastructure().Metadata.Validate();

            var targetModelBuilder = CreateModelBuilder();
            buildCommonAction(targetModelBuilder);
            buildTargetAction(targetModelBuilder);
            targetModelBuilder.GetInfrastructure().Metadata.Validate();

            var modelDiffer = CreateModelDiffer(targetModelBuilder.Model);

            var operationsUp = modelDiffer.GetDifferences(sourceModelBuilder.Model, targetModelBuilder.Model);
            assertActionUp(operationsUp);

            if (assertActionDown != null)
            {
                modelDiffer = CreateModelDiffer(sourceModelBuilder.Model);

                var operationsDown = modelDiffer.GetDifferences(targetModelBuilder.Model, sourceModelBuilder.Model);
                assertActionDown(operationsDown);
            }
        }

        protected abstract TestHelpers TestHelpers { get; }
        protected virtual ModelBuilder CreateModelBuilder() => TestHelpers.CreateConventionBuilder();
        protected virtual IModelValidator CreateModelValidator() => TestHelpers.CreateContextServices().GetRequiredService<IModelValidator>();

        protected virtual MigrationsModelDiffer CreateModelDiffer(IModel model)
        {
            var ctx = TestHelpers.CreateContext(
                TestHelpers.AddProviderOptions(new DbContextOptionsBuilder())
                    .UseModel(model).EnableSensitiveDataLogging().Options);
            return new MigrationsModelDiffer(
                new Microsoft.EntityFrameworkCore.TestUtilities.TestRelationalTypeMappingSource(
                    TestServiceFactory.Instance.Create<TypeMappingSourceDependencies>(),
                    TestServiceFactory.Instance.Create<RelationalTypeMappingSourceDependencies>()),
                new MigrationsAnnotationProvider(
                    new MigrationsAnnotationProviderDependencies()),
                ctx.GetService<IChangeDetector>(),
                ctx.GetService<StateManagerDependencies>(),
                ctx.GetService<CommandBatchPreparerDependencies>());
        }

        private class ConcreteTypeMapper : RelationalTypeMapper
        {
            public ConcreteTypeMapper(RelationalTypeMapperDependencies dependencies)
                : base(dependencies)
            {
            }

            protected override string GetColumnType(IProperty property) => property.TestProvider().ColumnType;

            public override RelationalTypeMapping FindMapping(Type clrType)
                => clrType == typeof(string)
                    ? new StringTypeMapping("varchar(4000)", dbType: null, unicode: false, size: 4000)
                    : base.FindMapping(clrType);

            protected override RelationalTypeMapping FindCustomMapping(IProperty property)
                => property.ClrType == typeof(string) && (property.GetMaxLength().HasValue || property.IsUnicode().HasValue)
                    ? new StringTypeMapping(((property.IsUnicode() ?? true) ? "n" : "") + "varchar(" + (property.GetMaxLength() ?? 767) + ")", dbType: null, unicode: false, size: property.GetMaxLength())
                    : base.FindCustomMapping(property);

            private readonly IReadOnlyDictionary<Type, RelationalTypeMapping> _simpleMappings
                = new Dictionary<Type, RelationalTypeMapping>
                {
                    { typeof(int), new IntTypeMapping("int") },
                    { typeof(bool), new BoolTypeMapping("boolean") }
                };

            private readonly IReadOnlyDictionary<string, RelationalTypeMapping> _simpleNameMappings
                = new Dictionary<string, RelationalTypeMapping>
                {
                    { "varchar", new StringTypeMapping("varchar", dbType: null, unicode: false, size: null) },
                    { "bigint", new LongTypeMapping("bigint") }
                };

            protected override IReadOnlyDictionary<Type, RelationalTypeMapping> GetClrTypeMappings()
                => _simpleMappings;

            protected override IReadOnlyDictionary<string, RelationalTypeMapping> GetStoreTypeMappings()
                => _simpleNameMappings;
        }
    }
}
