// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.Translations;
using Microsoft.EntityFrameworkCore.TestModels.BasicTypesModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Translations;

public class BasicTypesQueryJetFixture : BasicTypesQueryFixtureBase, ITestSqlLoggerFactory
{
    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;

    public TestSqlLoggerFactory TestSqlLoggerFactory
        => (TestSqlLoggerFactory)ListLoggerFactory;

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);

        modelBuilder.Entity<BasicTypesEntity>().Property(b => b.Decimal).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<NullableBasicTypesEntity>().Property(b => b.Decimal).HasColumnType("decimal(18,2)");
    }

    protected override Task SeedAsync(BasicTypesContext context)
    {
        var data = new BasicTypesData();
        //for every data.BasicTypesEntities and data.NullableBasicTypesEntities take the DateTime and DateTimeOffset and set the milliseconds to 0
        foreach (var entity in data.BasicTypesEntities)
        {
            entity.DateTime = new DateTime(entity.DateTime.Year, entity.DateTime.Month, entity.DateTime.Day, entity.DateTime.Hour, entity.DateTime.Minute, entity.DateTime.Second);
            entity.DateTimeOffset = new DateTimeOffset(entity.DateTimeOffset.Year, entity.DateTimeOffset.Month, entity.DateTimeOffset.Day, entity.DateTimeOffset.Hour, entity.DateTimeOffset.Minute, entity.DateTimeOffset.Second, entity.DateTimeOffset.Offset);
            entity.TimeOnly = new TimeOnly(entity.TimeOnly.Hour, entity.TimeOnly.Minute, entity.TimeOnly.Second);
            entity.TimeSpan = new TimeSpan(entity.TimeSpan.Days, entity.TimeSpan.Hours, entity.TimeSpan.Minutes, entity.TimeSpan.Seconds);
            if (entity.DateOnly.Year < 100)
            {
                entity.DateOnly = entity.DateOnly.AddYears(100); // Adjust for Jet's handling of DateOnly
            }
            if (entity.DateTime.Year < 100)
            {
                entity.DateTime = entity.DateTime.AddYears(100); // Adjust for Jet's handling of DateTime
            }
            if (entity.DateTimeOffset.Year < 100)
            {
                entity.DateTimeOffset = entity.DateTimeOffset.AddYears(100); // Adjust for Jet's handling of DateTimeOffset
            }
        }

        foreach (var entity in data.NullableBasicTypesEntities)
        {
            if (entity.DateTime.HasValue)
            {
                entity.DateTime = new DateTime(entity.DateTime.Value.Year, entity.DateTime.Value.Month, entity.DateTime.Value.Day, entity.DateTime.Value.Hour, entity.DateTime.Value.Minute, entity.DateTime.Value.Second);
            }
            if (entity.DateTimeOffset.HasValue)
            {
                entity.DateTimeOffset = new DateTimeOffset(entity.DateTimeOffset.Value.Year, entity.DateTimeOffset.Value.Month, entity.DateTimeOffset.Value.Day, entity.DateTimeOffset.Value.Hour, entity.DateTimeOffset.Value.Minute, entity.DateTimeOffset.Value.Second, entity.DateTimeOffset.Value.Offset);
            }
            if (entity.TimeOnly.HasValue)
            {
                entity.TimeOnly = new TimeOnly(entity.TimeOnly.Value.Hour, entity.TimeOnly.Value.Minute, entity.TimeOnly.Value.Second);
            }
            if (entity.TimeSpan.HasValue)
            {
                entity.TimeSpan = new TimeSpan(entity.TimeSpan.Value.Days, entity.TimeSpan.Value.Hours, entity.TimeSpan.Value.Minutes, entity.TimeSpan.Value.Seconds);
            }
            if (entity.DateOnly.HasValue && entity.DateOnly.Value.Year < 100)
            {
                entity.DateOnly = entity.DateOnly.Value.AddYears(100); // Adjust for Jet's handling of DateOnly
            }
            if (entity.DateTime.HasValue && entity.DateTime.Value.Year < 100)
            {
                entity.DateTime = entity.DateTime.Value.AddYears(100); // Adjust for Jet's handling of DateTime
            }
            if (entity.DateTimeOffset.HasValue && entity.DateTimeOffset.Value.Year < 100)
            {
                entity.DateTimeOffset = entity.DateTimeOffset.Value.AddYears(100); // Adjust for Jet's handling of DateTimeOffset
            }
        }

        context.AddRange(data.BasicTypesEntities);
        context.AddRange(data.NullableBasicTypesEntities);
        return context.SaveChangesAsync();
    }

    public override ISetSource GetExpectedData()
    {
        BasicTypesData result = (BasicTypesData)base.GetExpectedData();
        result.BasicTypesEntities.ForEach(b =>
        {
            b.DateTime = new DateTime(b.DateTime.Year, b.DateTime.Month, b.DateTime.Day, b.DateTime.Hour, b.DateTime.Minute, b.DateTime.Second);
            b.DateTimeOffset = new DateTimeOffset(b.DateTimeOffset.Year, b.DateTimeOffset.Month, b.DateTimeOffset.Day, b.DateTimeOffset.Hour, b.DateTimeOffset.Minute, b.DateTimeOffset.Second, b.DateTimeOffset.Offset);
            b.TimeOnly = new TimeOnly(b.TimeOnly.Hour, b.TimeOnly.Minute, b.TimeOnly.Second);
            b.TimeSpan = new TimeSpan(b.TimeSpan.Days, b.TimeSpan.Hours, b.TimeSpan.Minutes, b.TimeSpan.Seconds);
            if (b.DateOnly.Year < 100)
            {
                b.DateOnly = b.DateOnly.AddYears(100); // Adjust for Jet's handling of DateOnly
            }
            if (b.DateTime.Year < 100)
            {
                b.DateTime = b.DateTime.AddYears(100); // Adjust for Jet's handling of DateTime
            }
            if (b.DateTimeOffset.Year < 100)
            {
                b.DateTimeOffset = b.DateTimeOffset.AddYears(100); // Adjust for Jet's handling of DateTimeOffset
            }
        });

        result.NullableBasicTypesEntities.ForEach(b =>
        {
            if (b.DateTime.HasValue)
            {
                b.DateTime = new DateTime(b.DateTime.Value.Year, b.DateTime.Value.Month, b.DateTime.Value.Day, b.DateTime.Value.Hour, b.DateTime.Value.Minute, b.DateTime.Value.Second);
            }
            if (b.DateTimeOffset.HasValue)
            {
                b.DateTimeOffset = new DateTimeOffset(b.DateTimeOffset.Value.Year, b.DateTimeOffset.Value.Month, b.DateTimeOffset.Value.Day, b.DateTimeOffset.Value.Hour, b.DateTimeOffset.Value.Minute, b.DateTimeOffset.Value.Second, b.DateTimeOffset.Value.Offset);
            }
            if (b.TimeOnly.HasValue)
            {
                b.TimeOnly = new TimeOnly(b.TimeOnly.Value.Hour, b.TimeOnly.Value.Minute, b.TimeOnly.Value.Second);
            }
            if (b.TimeSpan.HasValue)
            {
                b.TimeSpan = new TimeSpan(b.TimeSpan.Value.Days, b.TimeSpan.Value.Hours, b.TimeSpan.Value.Minutes, b.TimeSpan.Value.Seconds);
            }
            if (b.DateOnly.HasValue && b.DateOnly.Value.Year < 100)
            {
                b.DateOnly = b.DateOnly.Value.AddYears(100); // Adjust for Jet's handling of DateOnly
            }
            if (b.DateTime.HasValue && b.DateTime.Value.Year < 100)
            {
                b.DateTime = b.DateTime.Value.AddYears(100); // Adjust for Jet's handling of DateTime
            }
            if (b.DateTimeOffset.HasValue && b.DateTimeOffset.Value.Year < 100)
            {
                b.DateTimeOffset = b.DateTimeOffset.Value.AddYears(100); // Adjust for Jet's handling of DateTimeOffset
            }
        });

        return result;
    }
}
