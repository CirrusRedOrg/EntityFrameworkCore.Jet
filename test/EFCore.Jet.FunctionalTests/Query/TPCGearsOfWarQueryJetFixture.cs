// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
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

    protected override async Task SeedAsync(GearsOfWarContext context)
    {
        // Drop constraint to workaround Jet limitation regarding compound foreign keys and NULL.
        context.Database.ExecuteSql($"ALTER TABLE `Officers` DROP CONSTRAINT `FK_Officers_Officers_LeaderNickname_LeaderSquadId`");

        var squads = GearsOfWarData.CreateSquads();
        var missions = GearsOfWarData.CreateMissions();
        var squadMissions = GearsOfWarData.CreateSquadMissions();
        var cities = GearsOfWarData.CreateCities();
        var weapons = GearsOfWarData.CreateWeapons();
        var tags = GearsOfWarData.CreateTags();
        var gears = GearsOfWarData.CreateGears();
        var locustLeaders = GearsOfWarData.CreateLocustLeaders();
        var factions = GearsOfWarData.CreateFactions();
        var locustHighCommands = GearsOfWarData.CreateHighCommands();

        foreach (var mission in missions)
        {
            if (mission.Timeline.Year < 100)
            {
                mission.Timeline = mission.Timeline.AddYears(100);
            }
            if (mission.Date.Year < 100)
            {
                mission.Date = mission.Date.AddYears(100);
            }
        }

        foreach (var tag in tags)
        {
            if (tag.IssueDate.Year < 100)
            {
                tag.IssueDate = tag.IssueDate.AddYears(100);
            }
        }

        GearsOfWarData.WireUp(
            squads, missions, squadMissions, cities, weapons, tags, gears, locustLeaders, factions, locustHighCommands);

        context.Squads.AddRange(squads);
        context.Missions.AddRange(missions);
        context.SquadMissions.AddRange(squadMissions);
        context.Cities.AddRange(cities);
        context.Weapons.AddRange(weapons);
        context.Tags.AddRange(tags);
        context.Gears.AddRange(gears);
        context.LocustLeaders.AddRange(locustLeaders);
        context.Factions.AddRange(factions);
        context.LocustHighCommands.AddRange(locustHighCommands);
        await context.SaveChangesAsync();

        GearsOfWarData.WireUp2(locustLeaders, factions);

        await context.SaveChangesAsync();
    }

    public override ISetSource GetExpectedData()
    {
        var data = (GearsOfWarData)base.GetExpectedData();

        foreach (var mission in data.Missions)
        {
            if (mission.Timeline.Year < 100)
            {
                mission.Timeline = mission.Timeline.AddYears(100);
            }
            if (mission.Date.Year < 100)
            {
                mission.Date = mission.Date.AddYears(100);
            }
            mission.Timeline = JetTestHelpers.GetExpectedValue(mission.Timeline);
            mission.Duration = new TimeSpan(mission.Duration.Days, mission.Duration.Hours, mission.Duration.Minutes, mission.Duration.Seconds);
        }

        foreach (var tag in data.Tags)
        {
            if (tag.IssueDate.Year < 100)
            {
                tag.IssueDate = tag.IssueDate.AddYears(100);
            }
        }

        return data;
    }
}
