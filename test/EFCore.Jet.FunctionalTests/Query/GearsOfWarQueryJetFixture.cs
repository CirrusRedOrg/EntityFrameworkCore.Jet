// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.GearsOfWarModel;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class GearsOfWarQueryJetFixture : GearsOfWarQueryRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);

            modelBuilder.Entity<City>().Property(g => g.Location).HasColumnType("varchar(100)");
        }

        protected override void Seed(GearsOfWarContext context)
        {
            // Drop constraint to workaround Jet limitation regarding compound foreign keys and NULL.
            context.Database.ExecuteSql($"ALTER TABLE `Gears` DROP CONSTRAINT `FK_Gears_Gears_LeaderNickname_LeaderSquadId`");

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
            context.SaveChanges();

            GearsOfWarData.WireUp2(locustLeaders, factions);

            context.SaveChanges();
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
                mission.Timeline = JetTestHelpers.GetExpectedValue(mission.Timeline);
            }

            return data;
        }
    }
}
