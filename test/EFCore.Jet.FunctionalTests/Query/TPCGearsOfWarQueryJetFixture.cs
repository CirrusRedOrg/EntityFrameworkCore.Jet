// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using EFCore.Jet.CustomBaseTests.GearsOfWarModel;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
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

        modelBuilder.Entity<Officer>().HasMany(o => o.Reports).WithOne().HasForeignKey(
            o => new { o.LeaderNickname, o.LeaderSquadId }).MatchSimple();

        // No support yet for DateOnly/TimeOnly (#24507)
        modelBuilder.Entity<Mission>(
            b =>
            {
                b.Ignore(m => m.Date);
                b.Ignore(m => m.Time);
            });
    }

    /*
         * We need to workaround a different behaviour with Jet.
         * When working with a foreign key declaration over multiple fields, each field must meet the condition.
         * This is different to other databases where if any field is null, the foreign key constaint is not required
         * PostgreSql call this MATCH SIMPLE and MATCH FULL is where each field individually must meet the constraint
         * SQL Server operates in MATCH SIMPLE mode
         * Jet differs and operates in MATCH FULL mode
         */
    public override ISetSource GetExpectedData()
    {
        var data = (GearsOfWarData)base.GetExpectedData();

        /*foreach (var mission in data.Missions)
        {
            mission.Timeline = mission.Timeline.AddYears(100);
        }
        */
        /*foreach (var gear in data.Gears)
        {
            if (gear.LeaderSquadId != 0) continue;
            gear.LeaderSquadId = 1;
            gear.LeaderNickname = "Marcus";
            ((Officer)gear).Reports.Add(gear);
        }*/
        return data;
    }

    protected override void Seed(GearsOfWarContext context)
    {
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
        /*foreach (var mission in missions)
        {
            mission.Timeline = new DateTimeOffset(new DateTime(1753, 1, 1));
        }*/
        /*foreach (var gear in gears)
        {
            if (gear.LeaderSquadId != 0) continue;
            gear.LeaderSquadId = 1;
            gear.LeaderNickname = "Marcus";
        }*/
        GearsOfWarData.WireUp(
            squads, missions, squadMissions, cities, weapons, tags, gears, locustLeaders, factions, locustHighCommands);

        /*foreach (var tag in tags)
        {
            tag.IssueDate = new DateTime(1750, 1, 1);
        }*/


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
        context.SaveChanges();

        GearsOfWarData.WireUp2(locustLeaders, factions);

        context.SaveChanges();
    }
}
