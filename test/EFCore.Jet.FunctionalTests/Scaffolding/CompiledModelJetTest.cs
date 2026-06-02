// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ReSharper disable InconsistentNaming

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure;
using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests.Scaffolding;

public class CompiledModelJetTest(NonSharedFixture fixture) : CompiledModelRelationalTestBase(fixture)
{
    protected override void BuildBigModel(ModelBuilder modelBuilder, bool jsonColumns)
    {
        base.BuildBigModel(modelBuilder, jsonColumns);

        modelBuilder
            .UseJetIdentityColumns(3, 2);

        modelBuilder.Entity<PrincipalBase>(eb =>
        {
            if (!jsonColumns)
            {
                eb.Property(e => e.Id).HasConversion<int>().UseJetIdentityColumn(2, 3).ValueGeneratedOnAdd();
            }

            eb.HasKey(e => new { e.Id, e.AlternateId });

            eb.OwnsOne(
                e => e.Owned, ob =>
                {
                    ob.Property(e => e.Details);

                    if (!jsonColumns)
                    {
                        ob.ToTable(
                            "PrincipalBase", "mySchema",
                            t => t.Property("PrincipalBaseId").UseJetIdentityColumn(2, 3));
                    }
                });
        });

        modelBuilder.Entity<PrincipalDerived<DependentBase<byte?>>>(eb =>
        {
            eb.HasMany(e => e.Principals).WithMany(e => (ICollection<PrincipalDerived<DependentBase<byte?>>>)e.Deriveds)
                .UsingEntity(jb =>
                {
                });
        });

        modelBuilder.Entity<ManyTypes>(eb =>
        {
            eb.Property(m => m.CharToStringConverterProperty)
                .IsFixedLength();
        });
    }

    protected override void AssertBigModel(IModel model, bool jsonColumns)
    {
        base.AssertBigModel(model, jsonColumns);
        /*Assert.Equal(
            [RelationalAnnotationNames.MaxIdentifierLength, JetAnnotationNames.ValueGenerationStrategy],
            model.GetAnnotations().Select(a => a.Name));*/
        Assert.Equal(JetValueGenerationStrategy.IdentityColumn, model.GetValueGenerationStrategy());
        Assert.Equal(
            CoreStrings.RuntimeModelMissingData,
            Assert.Throws<InvalidOperationException>(() => model.GetPropertyAccessMode()).Message);
        Assert.Equal(model[JetAnnotationNames.IdentitySeed], 3);
        Assert.Equal(model[JetAnnotationNames.IdentityIncrement], 2);

        var principalBase = model.FindEntityType(typeof(PrincipalBase))!;
        var principalId = principalBase.FindProperty(nameof(PrincipalBase.Id))!;
        Assert.Equal("integer", principalId.GetColumnType());
        if (jsonColumns)
        {
            Assert.Equal(
                [JetAnnotationNames.ValueGenerationStrategy],
                principalId.GetAnnotations().Select(a => a.Name));
            Assert.Equal(JetValueGenerationStrategy.None, principalId.GetValueGenerationStrategy());
        }
        else
        {
            /*Assert.Equal(
                [RelationalAnnotationNames.RelationalOverrides, JetAnnotationNames.ValueGenerationStrategy],
                principalId.GetAnnotations().Select(a => a.Name));*/
            Assert.Equal(JetValueGenerationStrategy.IdentityColumn, principalId.GetValueGenerationStrategy());
        }

        Assert.Equal(model[JetAnnotationNames.IdentitySeed], 3);
        Assert.Equal(model[JetAnnotationNames.IdentityIncrement], 2);

        var principalKey = principalBase.GetKeys().Last();

        var referenceOwnedNavigation = principalBase.GetNavigations().Single();
        var referenceOwnedType = referenceOwnedNavigation.TargetEntityType;

        var principalTable = StoreObjectIdentifier.Create(referenceOwnedType, StoreObjectType.Table)!.Value;
        if (jsonColumns)
        {
            Assert.Equal(
                JetValueGenerationStrategy.None,
                principalId.GetValueGenerationStrategy(principalTable));
        }
        else
        {
            Assert.Equal(
                JetValueGenerationStrategy.IdentityColumn,
                principalId.GetValueGenerationStrategy(principalTable));
        }

        var detailsProperty = referenceOwnedType.FindProperty(nameof(OwnedType.Details))!;

        var principalDerived = model.FindEntityType(typeof(PrincipalDerived<DependentBase<byte?>>))!;
        var ownedCollectionNavigation = principalDerived.GetDeclaredNavigations().Last();
        var collectionOwnedType = ownedCollectionNavigation.TargetEntityType;

        var derivedSkipNavigation = principalDerived.GetDeclaredSkipNavigations().Single();
        var joinType = derivedSkipNavigation.JoinEntityType;

        var rowid = joinType.GetProperties().Single(p => !p.IsForeignKey());
        Assert.Equal("varbinary(8)", rowid.GetColumnType());
        Assert.Equal(JetValueGenerationStrategy.None, rowid.GetValueGenerationStrategy());

        var manyTypesType = model.FindEntityType(typeof(ManyTypes))!;
        var stringProperty = manyTypesType.FindProperty(nameof(ManyTypes.String))!;
        Assert.True(stringProperty.FindRelationalTypeMapping()!.IsUnicode);
        Assert.False(stringProperty.FindRelationalTypeMapping()!.IsFixedLength);
        var charToStringConverterProperty = manyTypesType.FindProperty(nameof(ManyTypes.CharToStringConverterProperty))!;
        Assert.True(charToStringConverterProperty.FindRelationalTypeMapping()!.IsUnicode);
        Assert.True(charToStringConverterProperty.FindRelationalTypeMapping()!.IsFixedLength);

        var dependentNavigation = principalDerived.GetDeclaredNavigations().First();
        var dependentBase = dependentNavigation.TargetEntityType;
        var dependentDerived = dependentBase.GetDerivedTypes().Single();

        var dependentData = dependentDerived.GetDeclaredProperties().First();
        Assert.Equal("char(20)", dependentData.GetColumnType());

        var dependentMoney = dependentDerived.GetDeclaredProperties().Last();
        Assert.Equal("decimal(9,3)", dependentMoney.GetColumnType());
        Assert.Equal(
            [
                dependentBase,
                dependentDerived,
                manyTypesType,
                principalBase,
                referenceOwnedType,
                principalDerived,
                collectionOwnedType,
                joinType
            ],
            model.GetEntityTypes());
    }

    protected override void BuildComplexTypesModel(ModelBuilder modelBuilder)
    {
        base.BuildComplexTypesModel(modelBuilder);

        modelBuilder.Entity<PrincipalBase>(eb =>
        {
            eb.ComplexProperty(
                e => e.Owned, eb =>
                {
                    eb.Ignore(c => c.Context);

                    eb.Property(c => c.Details);
                });
        });
    }

    protected override void AssertComplexTypes(IModel model)
    {
        base.AssertComplexTypes(model);

        var principalBase = model.FindEntityType(typeof(PrincipalBase))!;
        var complexProperty = principalBase.GetComplexProperties().Single();
        var complexType = complexProperty.ComplexType;
        var detailsProperty = complexType.FindProperty(nameof(OwnedType.Details))!;
        Assert.Equal(
            [
                CoreAnnotationNames.MaxLength,
                CoreAnnotationNames.Precision,
                RelationalAnnotationNames.ColumnName,
                RelationalAnnotationNames.ColumnType,
                CoreAnnotationNames.Scale,
                JetAnnotationNames.ValueGenerationStrategy,
                CoreAnnotationNames.Unicode,
                "foo"
            ],
            detailsProperty.GetAnnotations().Select(a => a.Name));

        var dbFunction = model.FindDbFunction("PrincipalBaseTvf")!;
        Assert.Equal("dbo", dbFunction.Schema);

        Assert.Equal(JetValueGenerationStrategy.None, detailsProperty.GetValueGenerationStrategy());
        Assert.Equal(
            CoreStrings.RuntimeModelMissingData,
            Assert.Throws<InvalidOperationException>(() => detailsProperty.GetJetIdentitySeed()).Message);
        Assert.Equal(
            CoreStrings.RuntimeModelMissingData,
            Assert.Throws<InvalidOperationException>(() => detailsProperty.GetJetIdentityIncrement()).Message);
    }

    /*public override Task BigModel_with_JSON_columns()
        => Task.CompletedTask;

    //Sprocs not supported
    public override Task ComplexTypes()
        => Task.CompletedTask;

    //Not supported
    public override Task Sequences()
        => Task.CompletedTask;

    //Sprocs not supported
    public override Task Tpc_Sprocs()
        => Task.CompletedTask;

    public override Task Triggers()
        => Task.CompletedTask;

    public override Task DbFunctions()
        => Task.CompletedTask;*/

    protected override TestHelpers TestHelpers
        => JetTestHelpers.Instance;

    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;

    protected override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
    {
        builder = base.AddOptions(builder)
            .ConfigureWarnings(w => w.Ignore(JetEventId.DecimalTypeDefaultWarning));
        new JetDbContextOptionsBuilder(builder);
        return builder;
    }

    protected override BuildSource AddReferences(BuildSource build, [CallerFilePath] string filePath = "")
    {
        base.AddReferences(build);
        build.References.Add(BuildReference.ByName("EntityFrameworkCore.Jet"));
        return build;
    }
}
