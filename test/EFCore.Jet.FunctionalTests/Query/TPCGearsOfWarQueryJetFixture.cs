// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.GearsOfWarModel;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

public class TPCGearsOfWarQueryJetFixture : TPCGearsOfWarQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);

        modelBuilder.Entity<City>().Property(g => g.Location).HasColumnType("varchar(100)");
    }

    protected override void Seed(GearsOfWarContext context)
    {
        // Drop constraint to workaround Jet limitation regarding compound foreign keys and NULL.
        context.Database.ExecuteSql($"ALTER TABLE `Officers` DROP CONSTRAINT `FK_Officers_Officers_LeaderNickname_LeaderSquadId`");

        base.Seed(context);
    }

    public new ISetSource GetExpectedData()
    {
        var data = (GearsOfWarData)base.GetExpectedData();

        foreach (var mission in data.Missions)
        {
            mission.Timeline = JetTestHelpers.GetExpectedValue(mission.Timeline);
        }

        return data;
    }
}
